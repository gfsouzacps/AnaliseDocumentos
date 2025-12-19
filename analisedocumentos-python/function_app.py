import logging
import os
import mimetypes
import uuid
import functools
import base64
from datetime import datetime

import azure.functions as func
import azure.durable_functions as df
from azure.identity.aio import DefaultAzureCredential
from azure.storage.blob.aio import BlobServiceClient

from shared_code.models import ResultadoAnaliseDocumento
from shared_code.services import OcrService, FoundryService

# --- CONFIGURAÇÃO DE LOGGING ---
# Mantemos isso para garantir que o worker Python não abuse dos logs
logging.getLogger("azure").setLevel(logging.WARNING)
logging.getLogger("azure.core").setLevel(logging.WARNING)
logging.getLogger("azure.identity").setLevel(logging.WARNING)
logging.getLogger("urllib3").setLevel(logging.WARNING)
logging.getLogger("msal").setLevel(logging.WARNING)
# -------------------------------

app = df.DFApp(http_auth_level=func.AuthLevel.FUNCTION)

# --- LAZY LOADING DE CLIENTES ---
# Isso impede que a autenticação rode no momento do import do arquivo (Startup)
# Evita o "boot storm" que está travando seu app.

@functools.lru_cache(maxsize=1)
def get_ocr_service():
    return OcrService()

@functools.lru_cache(maxsize=1)
def get_foundry_service():
    return FoundryService()

@functools.lru_cache(maxsize=1)
def get_blob_service_client():
    storage_account_url = os.environ.get("AzureWebJobsStorage__blobServiceUri")
    if storage_account_url:
        # Nota: Credential é criada aqui, não globalmente
        return BlobServiceClient(account_url=storage_account_url, credential=DefaultAzureCredential())
    return None
# -------------------------------

# HTTP Trigger
@app.route(route="SubmeterDocumento")
@app.durable_client_input(client_name="durable_client")
async def submeter_documento(req: func.HttpRequest, durable_client: df.DurableOrchestrationClient) -> func.HttpResponse:
    logging.info("Receiving document to start analysis...")

    file_bytes = req.get_body()
    if not file_bytes:
        return func.HttpResponse("Request body is empty.", status_code=400)

    try:
        content_type = req.headers.get("Content-Type", "application/octet-stream")
        
        orchestrator_input = {
            "file_bytes": base64.b64encode(file_bytes).decode('utf-8'),
            "content_type": content_type
        }

        instance_id = await durable_client.start_new("OrquestradorDocumento", client_input=orchestrator_input)
        logging.info(f"Orchestration started: {instance_id}")

        return durable_client.create_check_status_response(req, instance_id)

    except Exception as e:
        logging.error(f"Error in 'submeter_documento': {e}", exc_info=True)
        return func.HttpResponse(f"Internal Server Error: {e}", status_code=500)

# Orchestrator Function
@app.orchestration_trigger(context_name="context")
def OrquestradorDocumento(context: df.DurableOrchestrationContext):
    orchestrator_input = context.get_input()
    if not orchestrator_input or "file_bytes" not in orchestrator_input:
        raise ValueError("Input is null or missing file content.")

    # Fan-out
    ocr_task = context.call_activity("Activity_ExtrairTexto", orchestrator_input)
    save_task = context.call_activity("Activity_SalvarDocumento", orchestrator_input)
    
    texto_extraido, blob_uri = yield context.task_all([ocr_task, save_task])

    # Foundry Analysis
    json_analise = yield context.call_activity("Activity_AnalisarFoundry", texto_extraido)
    
    resultado = ResultadoAnaliseDocumento(
        contrato_id=context.instance_id,
        json_analise=json_analise,
        texto_puro=texto_extraido,
        blob_uri=blob_uri,
        processado_em=context.current_utc_datetime
    )
    return resultado.asdict() if hasattr(resultado, 'asdict') else vars(resultado)


# Activity: Extract Text
@app.activity_trigger(input_name="doc_input")
def Activity_ExtrairTexto(doc_input: dict) -> str:
    file_bytes = base64.b64decode(doc_input["file_bytes"])
    content_type = doc_input["content_type"]
    logging.info(f"Executing OCR activity...")
    try:
        # Chamada lazy do serviço
        service = get_ocr_service()
        return service.extrair_texto(file_bytes, content_type)
    except Exception as e:
        logging.error(f"Error in OCR activity: {e}", exc_info=True)
        raise

# Activity: Save document
@app.activity_trigger(input_name="doc_input")
async def Activity_SalvarDocumento(doc_input: dict) -> str:
    # Chamada lazy do serviço
    blob_client_svc = get_blob_service_client()
    
    if not blob_client_svc:
        logging.warning("BlobServiceClient not initialized. Skipping archival.")
        return None

    file_bytes = base64.b64decode(doc_input["file_bytes"])
    content_type = doc_input["content_type"]
    extension = mimetypes.guess_extension(content_type) or ".bin"
    blob_name = f"{uuid.uuid4()}{extension}"
    
    logging.info(f"Archiving document to blob: {blob_name}")
    try:
        container_client = blob_client_svc.get_container_client("documentos-arquivo")
        await container_client.create_container(fail_on_exist=False)
        
        blob_client = container_client.get_blob_client(blob_name)
        await blob_client.upload_blob(file_bytes, content_settings={'content_type': content_type})
        
        return blob_client.url
    except Exception as e:
        logging.error(f"Error in archival activity: {e}", exc_info=True)
        raise

# Activity: Analyze with Foundry
@app.activity_trigger(input_name="texto")
def Activity_AnalisarFoundry(texto: str) -> str:
    logging.info("Executing Foundry analysis activity...")
    try:
        # Chamada lazy do serviço
        service = get_foundry_service()
        return service.analisar_texto(texto)
    except Exception as e:
        logging.error(f"Error in Foundry activity: {e}", exc_info=True)
        raise