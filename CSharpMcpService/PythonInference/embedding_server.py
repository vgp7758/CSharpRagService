"""
EmbeddingGemma 推理服务器
为 C# MCP 服务提供高质量的文本嵌入
"""

import torch
import torch.nn as nn
from transformers import AutoTokenizer, AutoModel
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Optional
import uvicorn
import numpy as np
from fastapi.middleware.cors import CORSMiddleware

# 创建 FastAPI 应用
app = FastAPI(title="EmbeddingGemma Service", version="1.0.0")

# 添加 CORS 中间件
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# 请求/响应模型
class EmbeddingRequest(BaseModel):
    texts: List[str]
    normalize: bool = True

class EmbeddingResponse(BaseModel):
    embeddings: List[List[float]]
    model: str
    inference_time: Optional[float] = None

class HealthResponse(BaseModel):
    status: str
    model: str
    device: str

# 全局变量
model = None
tokenizer = None
device = None
MODEL_NAME = "google/embeddinggemma-300m"

def load_model():
    """加载模型和分词器"""
    global model, tokenizer, device

    try:
        # 检查 CUDA 可用性
        device = "cuda" if torch.cuda.is_available() else "cpu"
        print(f"Using device: {device}")

        # 加载模型和分词器
        print(f"Loading model: {MODEL_NAME}")
        tokenizer = AutoTokenizer.from_pretrained(MODEL_NAME)
        model = AutoModel.from_pretrained(MODEL_NAME)

        # 移动到设备并设置为评估模式
        model = model.to(device)
        model.eval()

        print("Model loaded successfully")
        return True

    except Exception as e:
        print(f"Error loading model: {e}")
        return False

def get_text_embedding(text: str, normalize: bool = True) -> List[float]:
    """获取文本嵌入向量"""
    # 添加特殊标记
    text = f"[{text}]"

    # 分词
    inputs = tokenizer(text, return_tensors="pt", padding=True, truncation=True, max_length=512)
    inputs = {k: v.to(device) for k, v in inputs.items()}

    # 获取嵌入
    with torch.no_grad():
        outputs = model(**inputs)

    # 使用 [CLS] 标记的嵌入作为句子嵌入
    embeddings = outputs.last_hidden_state[:, 0, :].cpu().numpy()

    # 转换为列表
    embedding = embeddings[0].tolist()

    # 归一化
    if normalize:
        norm = np.linalg.norm(embedding)
        if norm > 0:
            embedding = [x / norm for x in embedding]

    return embedding

@app.on_event("startup")
async def startup_event():
    """启动时加载模型"""
    success = load_model()
    if not success:
        raise Exception("Failed to load model")

@app.get("/health", response_model=HealthResponse)
async def health_check():
    """健康检查"""
    return HealthResponse(
        status="healthy",
        model=MODEL_NAME,
        device=device
    )

@app.post("/embed", response_model=EmbeddingResponse)
async def get_embeddings(request: EmbeddingRequest):
    """获取文本嵌入向量"""
    try:
        import time
        start_time = time.time()

        # 批量处理文本
        embeddings = []
        for text in request.texts:
            embedding = get_text_embedding(text, request.normalize)
            embeddings.append(embedding)

        inference_time = time.time() - start_time

        return EmbeddingResponse(
            embeddings=embeddings,
            model=MODEL_NAME,
            inference_time=inference_time
        )

    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error generating embeddings: {str(e)}")

@app.get("/")
async def root():
    """根路径"""
    return {
        "message": "EmbeddingGemma Service",
        "model": MODEL_NAME,
        "endpoints": ["/health", "/embed"],
        "usage": "POST /embed with {'texts': ['text1', 'text2'], 'normalize': true}"
    }

if __name__ == "__main__":
    # 启动服务器
    uvicorn.run(
        "embedding_server:app",
        host="0.0.0.0",
        port=8000,
        reload=False,
        workers=1
    )