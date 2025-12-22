import json
import logging
import os
import mimetypes
import uuid
import functools
from datetime import datetime, timedelta

# Importações Azure
import azure.functions as func
import azure.durable_functions as df
from azure.identity.aio import DefaultAzureCredential
from azure.core.exceptions import ResourceExistsError
from azure.storage.blob import BlobSasPermissions, generate_blob_sas, ContentSettings
from azure.storage.blob.aio import BlobServiceClient

# Importações locais
from shared_code.models import ResultadoAnaliseDocumento
from shared_code.services import OcrService, FoundryService

# --- CONFIGURAÇÃO DE LOGGING ---
logging.getLogger("azure").setLevel(logging.WARNING)
logging.getLogger("azure.core").setLevel(logging.WARNING)
logging.getLogger("azure.identity").setLevel(logging.WARNING)
logging.getLogger("urllib3").setLevel(logging.WARNING)
logging.getLogger("msal").setLevel(logging.WARNING)
# -------------------------------

app = df.DFApp(http_auth_level=func.AuthLevel.FUNCTION)

# --- LAZY LOADING DE CLIENTES ---
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
        return BlobServiceClient(account_url=storage_account_url, credential=DefaultAzureCredential())
    logging.error("AzureWebJobsStorage__blobServiceUri not found in environment variables.")
    return None

# --- HELPERS ---
def _get_extension_from_mime_type(mime_type: str) -> str:
    mime_map = {
        "application/pdf": ".pdf",
        "image/jpeg": ".jpg",
        "image/jpg": ".jpg",
        "image/png": ".png",
        "image/tiff": ".tiff",
        "image/bmp": ".bmp",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document": ".docx",
        "application/msword": ".doc",
    }
    return mime_map.get(mime_type.lower(), ".bin")

# HTTP Trigger
@app.route(route="SubmeterDocumento")
@app.durable_client_input(client_name="durable_client")
async def submeter_documento(req: func.HttpRequest, durable_client: df.DurableOrchestrationClient) -> func.HttpResponse:
    logging.info("Receiving document to start analysis (Claim Check pattern)...")

    file_bytes = req.get_body()
    if not file_bytes:
        return func.HttpResponse("Request body is empty.", status_code=400)

    blob_service_client = get_blob_service_client()
    if not blob_service_client:
        return func.HttpResponse("Blob storage client could not be initialized.", status_code=500)

    try:
        content_type = req.headers.get("Content-Type", "application/octet-stream")
        extension = _get_extension_from_mime_type(content_type)
        
        # Criação segura do container (Idempotente)
        container_client = blob_service_client.get_container_client("documentos-upload")
        try:
            await container_client.create_container()
        except ResourceExistsError:
            pass # Container já existe
        
        blob_name = f"{uuid.uuid4()}{extension}"
        blob_client = container_client.get_blob_client(blob_name)
        
        logging.info(f"Uploading to blob: {blob_name} (Content-Type: {content_type})")
        
        # Upload
        await blob_client.upload_blob(file_bytes, overwrite=True, content_settings=ContentSettings(content_type=content_type))
        
        logging.info("Upload complete. Generating SAS URL...")

        # Gera SAS URL com User Delegation Key
        user_delegation_key = await blob_service_client.get_user_delegation_key(
            key_start_time=datetime.utcnow() - timedelta(minutes=5),
            key_expiry_time=datetime.utcnow() + timedelta(days=1)
        )

        sas_token = generate_blob_sas(
            account_name=blob_service_client.account_name,
            container_name=container_client.container_name,
            blob_name=blob_name,
            user_delegation_key=user_delegation_key,
            permission=BlobSasPermissions(read=True),
            expiry=datetime.utcnow() + timedelta(days=1)
        )
        
        # URL completa com SAS Token para o OCR acessar
        sas_uri = f"{blob_client.url}?{sas_token}"

        instance_id = await durable_client.start_new("OrquestradorDocumento", client_input=sas_uri)
        logging.info(f"Orchestration started with ID: {instance_id}")

        return durable_client.create_check_status_response(req, instance_id)

    except Exception as e:
        logging.error(f"Error in 'submeter_documento': {e}", exc_info=True)
        return func.HttpResponse(f"Internal Server Error: {e}", status_code=500)


# Orchestrator Function
@app.orchestration_trigger(context_name="context")
def OrquestradorDocumento(context: df.DurableOrchestrationContext):
    # Input agora é a URL do blob (com SAS)
    url_arquivo_sas = context.get_input()
    if isinstance(url_arquivo_sas, str):
        url_arquivo_sas = url_arquivo_sas.strip('"')
    if not url_arquivo_sas:
        raise ValueError("Input is null or missing file URL.")

    # Activity 1: OCR (passa a URL SAS)
    texto_extraido = yield context.call_activity("Activity_ExtrairTexto", url_arquivo_sas)

    # Activity 2: Foundry (recebe texto)
    json_analise = yield context.call_activity("Activity_AnalisarFoundry", texto_extraido)
    
    # Remove a Query String (SAS Token) para salvar no banco/retorno, por segurança
    blob_uri_limpa = url_arquivo_sas.split('?')[0] if '?' in url_arquivo_sas else url_arquivo_sas

    resultado = ResultadoAnaliseDocumento(
        contrato_id=context.instance_id,
        json_analise=json_analise,
        texto_puro=texto_extraido,
        blob_uri=blob_uri_limpa,
        processado_em=context.current_utc_datetime
    )
    return json.dumps(vars(resultado), default=str)


# Activity: Extract Text
@app.activity_trigger(input_name="url_arquivo")
def Activity_ExtrairTexto(url_arquivo: str) -> str:
    logging.info(f"Executing OCR activity from URL...")
    try:
        service = get_ocr_service()
        # Agora chama o método novo que vamos criar no services.py
        return service.extrair_texto_de_url(url_arquivo)
    except Exception as e:
        logging.error(f"Error in OCR activity: {e}", exc_info=True)
        raise

# Activity: Analyze with Foundry
@app.activity_trigger(input_name="texto")
def Activity_AnalisarFoundry(texto: str) -> str:
    logging.info("Executing Foundry analysis activity...")
    try:
        service = get_foundry_service()
        return service.analisar_texto(texto)
    except Exception as e:
        logging.error(f"Error in Foundry activity: {e}", exc_info=True)
        raise