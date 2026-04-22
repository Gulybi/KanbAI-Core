---
name: security-safety
description: Mandatory security protocols, input validation rules, and secret management. Applies to all code modifications.
---

# Security & Safety Standards

These rules dictate the security baseline for all development. Security is non-negotiable and must be prioritized in every implementation.

## 1. 🛑 Secret Management
- **No Hardcoded Secrets:** NEVER hardcode passwords, API keys, JWT secrets, connection strings, or any sensitive tokens in the source code.
- **Configuration:** Always retrieve secrets via `IConfiguration`, environment variables, or a secure secret manager.
- **Validation:** Check that `appsettings.json` and `appsettings.Development.json` do not contain production secrets.

## 2. 🛡️ Input Validation & Sanitization
- **Never Trust Client Input:** All incoming data from APIs, UI, or external systems must be strictly validated before any business logic is executed.
- **Validation Layers:** Use standard libraries (e.g., FluentValidation or DataAnnotations) to validate DTOs, Commands, and Queries.
- **Injection Prevention:** Rely on Entity Framework Core's parameterized queries by default. If raw SQL is absolutely necessary (e.g., using `.FromSqlRaw`), NEVER use string interpolation or concatenation for user inputs.

## 3. 🤐 Data Privacy & Logging
- **No PII in Logs:** Ensure that Personally Identifiable Information (PII) such as plain-text passwords, credit card numbers, or sensitive user data is NEVER recorded in application logs.
- **Secure DTOs:** Never return password hashes, internal system states, or sensitive domain data in API responses. Always map entities to safe DTOs.
- **Password Handling:** Always hash passwords using a secure algorithm (e.g., BCrypt, Argon2). Never store plain-text passwords.

## 4. 🌐 Web Security (OWASP Basics)
- **Authorization & Authentication:** Always ensure proper authorization checks (e.g., `[Authorize]` attributes, MediatR pipeline behaviors, or policy-based checks) are in place before allowing access to resources.
- **Data Exposure:** Prevent mass assignment vulnerabilities by strictly defining properties in DTOs rather than binding directly to domain entities.
- **CORS:** Configure CORS properly — do not use wildcard (`*`) origins in production.
- **HTTPS:** Ensure HTTPS is enforced in production environments.

## 5. 🔐 Authentication Best Practices
- **Token Security:** Use secure, cryptographically strong tokens for authentication.
- **Session Management:** Implement proper session timeout and renewal mechanisms.
- **Credential Storage:** Use environment-specific configuration for authentication secrets.

## 6. ⚠️ Common Vulnerabilities to Avoid
- **SQL Injection:** Always use parameterized queries (EF Core does this by default).
- **XSS (Cross-Site Scripting):** Sanitize output when rendering user-generated content.
- **CSRF (Cross-Site Request Forgery):** Use anti-forgery tokens for state-changing operations.
- **Path Traversal:** Validate file paths and never allow user input to directly control file system operations.
- **Insecure Deserialization:** Be cautious when deserializing user-controlled data.
