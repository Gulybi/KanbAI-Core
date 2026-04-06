# KanbAI - Backend (Core API)

## 📌 Project Purpose & Outcome
**KanbAI** is the backend service for a Kanban-style project management platform. Beyond standard software development, the primary goal of this project is to objectively analyze the efficiency, code quality, and engineering usability of AI-based coding assistants (e.g., Cursor, GitHub Copilot, Claude) in a simulated production environment as part of a university thesis.

The outcome is a stable, RESTful Web API that serves the Angular frontend client, manages the SQL database, and provides real-time communication between users.

The development follows a **dual methodology**:
1. **Reference Module (Manual):** The JWT-based authentication and registration system is built entirely manually, without AI assistance. This serves as a baseline for subsequent measurements.
2. **AI-Driven Modules:** More complex system components (e.g., Kanban drag-and-drop business logic, asynchronous file attachments, SignalR communication) are developed using dedicated AI tools.

## 🛠️ Tech Stack
* **Framework:** .NET Web API (C#)
* **ORM / Database Access:** Entity Framework Core (EF Core)
* **Database:** Microsoft SQL Server (MSSQL)
* **Real-Time Communication:** SignalR
* **Authentication:** JWT (JSON Web Token)
* **File Storage:** Local storage (initially) / MinIO integration planned

## 🏗️ Core Features & Architecture
The backend provides the following functional outcomes:
* **User & Permissions Management:** Secure login, registration, and JWT token generation.
* **Project Management:** Creating projects, assigning members (N:M relationship), and validating user access rights.
* **Kanban Logic:** Managing board columns and task cards, ensuring proper vertical and horizontal ordering (`TaskOrder`).
* **Real-Time Synchronization:** Broadcasting board updates instantly to connected clients via SignalR Hubs.
* **File Attachments:** Handling asynchronous file uploads and storing metadata linked to specific task cards.
* **Task Comments:** Managing comment threads attached to individual tasks.
