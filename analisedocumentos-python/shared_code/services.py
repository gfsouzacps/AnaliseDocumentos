import logging
import os
import time
from azure.identity import DefaultAzureCredential
from azure.ai.documentintelligence import DocumentIntelligenceClient
from azure.ai.documentintelligence.models import AnalyzeResult, AnalyzeDocumentRequest
from azure.ai.projects import AIProjectClient
# Importação segura de modelos para evitar erros se a lib mudar
from azure.ai.agents.models import MessageRole

def _limpar_json_markdown(json_com_markdown: str) -> str:
    if not json_com_markdown:
        return ""
    return json_com_markdown.replace("```json", "").replace("```", "").strip()

class OcrService:
    def __init__(self):
        endpoint = os.environ.get("DocIntelEndpoint")
        if not endpoint:
            logging.warning("DocIntelEndpoint not set. OCR will fail if called.")
            self._client = None
            return
        self._client = DocumentIntelligenceClient(endpoint=endpoint, credential=DefaultAzureCredential())

    def extrair_texto(self, file_bytes: bytes, content_type: str) -> str:
        if not self._client:
            raise ValueError("OCR Service not initialized properly (missing endpoint).")

        logging.info(f"Starting OCR from byte stream...")
        
        request = AnalyzeDocumentRequest(
            bytes_source=file_bytes
        )
        
        poller = self._client.begin_analyze_document("prebuilt-layout", request, content_type=content_type)
        resultado: AnalyzeResult = poller.result()
        return "\n".join([p.content for p in resultado.paragraphs]) if resultado.paragraphs else ""

    def extrair_texto_de_url(self, url_arquivo: str) -> str:
        if not self._client:
            raise ValueError("OCR Service not initialized properly (missing endpoint).")
        
        logging.info(f"Starting OCR from URL...")
        poller = self._client.begin_analyze_document_from_url(
            "prebuilt-layout", document_url=url_arquivo
        )
        resultado: AnalyzeResult = poller.result()
        return "\n".join([p.content for p in resultado.paragraphs]) if resultado.paragraphs else ""

class FoundryService:
    def __init__(self):
        # Captura apenas a URL do endpoint
        project_endpoint = os.environ.get("FoundryProjectEndpoint")
        self._agent_id = os.environ.get("FoundryAgentId")

        if not project_endpoint or not self._agent_id:
            logging.warning("Foundry env vars not set. Agent will fail if called.")
            self.project_client = None
            return

        # CORREÇÃO: Usar construtor por endpoint, não connection string
        self.project_client = AIProjectClient(
            endpoint=project_endpoint,
            credential=DefaultAzureCredential()
        )

    def analisar_texto(self, texto: str) -> str:
        if not self.project_client:
            raise ValueError("Foundry Service not initialized properly.")

        logging.info(f"Starting analysis with agent ID: {self._agent_id}")
        agents_client = self.project_client.agents

        # 1. Create thread
        thread = agents_client.create_thread()
        
        # 2. Add message to thread
        agents_client.create_message(
            thread_id=thread.id,
            role=MessageRole.USER,
            content=texto
        )

        # 3. Run
        run = agents_client.create_run(
            thread_id=thread.id,
            assistant_id=self._agent_id
        )

        # 4. Poll
        while run.status in ["queued", "in_progress", "requires_action"]:
            time.sleep(1)
            run = agents_client.get_run(thread_id=thread.id, run_id=run.id)
        
        if run.status != "completed":
            error_message = run.last_error if hasattr(run, 'last_error') else "Unknown error."
            raise Exception(f"Run failed: {run.status}. Error: {error_message}")

        # 5. Get messages
        messages = agents_client.list_messages(thread_id=thread.id)
        
        for msg in messages.data:
            if msg.role == MessageRole.AGENT:
                response_text = ""
                for content_item in msg.content:
                    if hasattr(content_item, 'text') and hasattr(content_item.text, 'value'):
                        response_text += content_item.text.value
                    elif hasattr(content_item, 'text'):
                         response_text += content_item.text
                return _limpar_json_markdown(response_text)
        
        return ""