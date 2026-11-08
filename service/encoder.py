"""Optional NF4 text encoder for 8GB GPUs. Uses the official LLM2Vec architecture/adapters.

Quantization changes embedding numerics; compare motion quality with the official
BF16 encoder before using this option for production assets.
"""
import torch
from transformers import BitsAndBytesConfig
from kimodo.model.llm2vec.llm2vec_wrapper import LLM2VecEncoder
from kimodo.model.llm2vec.llm2vec import LLM2Vec


class QuantizedEncoder(LLM2VecEncoder):
    def __init__(self):
        self.llm_dim = 4096
        self._device = "cuda"
        self.model = LLM2Vec.from_pretrained(
            base_model_name_or_path="McGill-NLP/LLM2Vec-Meta-Llama-3-8B-Instruct-mntp",
            peft_model_name_or_path="McGill-NLP/LLM2Vec-Meta-Llama-3-8B-Instruct-mntp-supervised",
            torch_dtype=torch.bfloat16,
            device_map={"": 0},
            quantization_config=BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4",
                                                  bnb_4bit_use_double_quant=True,
                                                  bnb_4bit_compute_dtype=torch.bfloat16),
        )
        self.model.eval()
        for param in self.model.parameters(): param.requires_grad = False
