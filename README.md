# ⚖️ Legal Reference Platform with RAG & PDF Integrity

A modular, scalable, platform-agnostic system designed to ingest, store, search, annotate, and reference thousands of legal PDF documents using state-of-the-art Retrieval-Augmented Generation (RAG) and explainable AI.

---

## 📌 Objectives

- Ingest and process scanned/legal PDF documents
- Enable keyword + semantic (RAG) search
- Maintain original PDFs as source of truth
- Provide annotation and redlining features for researchers
- Ensure legal-grade auditability, explainability, and integrity
- Support document versioning and collaborative commentary
- Run on any cloud or on-prem (API-first, UI-agnostic)

---

## 🏗️ Architecture Overview

```
User (Web/Mobile)
   ↓
.NET 8 API
   ├── Ingest Service
   ├── RAG Search Service
   ├── Annotation Service
   ├── Indexing Service (Elasticsearch/OpenSearch)
   ↓
PostgreSQL | Qdrant | OpenSearch | Blob Storage
```

---

## 🧩 Core Components

### ✅ Ingest Service
- OCR via Tesseract
- Metadata extraction via Apache Tika
- Document chunking + embedding
- Async embedding and indexing pipeline

### ✅ Document & Metadata Store
- PostgreSQL / MongoDB
- Stores document metadata, chunks, version trees, annotations

### ✅ Search Service (RAG + Hybrid)
- Embeds queries
- Retrieves relevant chunks via vector DB
- Optional LLM query with citations

### ✅ Indexing Service
- Sends chunks to OpenSearch/Elasticsearch
- Enables full-text keyword and hybrid search

### ✅ Blob Storage
- Stores original PDFs
- Provides download/view links
- Supports versioning, watermarking, and hashing

### ✅ Annotation & Redlining Service
- Highlighting, threaded comments, redline diffs
- Supports shared/team comments, approval flows

---

## 🧠 RAG + Explainability Flow

```
User Query
   ↓
Embed → Vector Search (top-K chunks)
   ↓
Join with Metadata (title, jurisdiction, PDF URL, page)
   ↓
Send to LLM (optional)
   ↓
LLM Response:
"See UCC § 3-305, Page 2, Paragraph 3"
```

---

## 🧱 Schema Highlights

```sql
Documents
- Id, Title, Jurisdiction, PdfUrl, Version, ParentDocumentId

Chunks
- Id, DocumentId, Page, Text, Embedding[], StartOffset, EndOffset

Annotations
- Id, DocumentId, ChunkId, Page, Text, UserId, IsShared, IsApproved

Users
- Id, Name, Role, Email
```

---

## 🚀 Deployment Targets

| Component        | Recommended Stack Options                    |
|------------------|-----------------------------------------------|
| API              | .NET 8, REST or GraphQL                       |
| Vector Store     | Qdrant, Weaviate, OpenSearch kNN              |
| Full-Text Search | OpenSearch / Elasticsearch                   |
| Embedding Models | OpenAI, HuggingFace Transformers, LM Studio   |
| Blob Storage     | S3, Azure Blob, MinIO, Local + Nginx          |
| OCR              | Dockerized Apache Tika + Tesseract            |

---

## 🖥️ Frontend Integration

Supports any framework:
- Angular, React, Blazor, Flutter, Native Apps

Core endpoints:
- `GET /documents`
- `POST /query`
- `GET /documents/{id}/chunks`
- `POST /annotations`
- `GET /pdf/{id}`

---

## 📌 Future Enhancements

- Access logs and audit trails
- Rule-based LLM routing (private/public)
- Admin interface for version control and metadata tagging
- Export annotated PDFs
- Support for citation chaining and law graph generation

---

## 🧾 License

This project is designed to be modular and extensible. Licensing may depend on included libraries (e.g., OpenAI API, Elasticsearch SSPL, etc.).

---

## 🤝 Contributions

Pull requests welcome! For major changes, please open an issue first to discuss what you’d like to change.

---



---
