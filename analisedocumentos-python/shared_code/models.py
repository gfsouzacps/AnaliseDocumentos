from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional

@dataclass
class ResultadoAnaliseDocumento:
    """
    Represents the result of a document analysis orchestration.
    """
    contrato_id: str = ""
    json_analise: str = ""
    texto_puro: str = "" # Optional, for debugging
    blob_uri: Optional[str] = None # Optional URI for the archived blob
    processado_em: datetime = field(default_factory=datetime.utcnow)
