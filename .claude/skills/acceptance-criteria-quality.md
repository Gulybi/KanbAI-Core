---
name: acceptance-criteria-quality
description: Guidelines for writing high-quality acceptance criteria that are testable, specific, independent, implementation-free, and complete.
---

# Acceptance Criteria Quality Standards

## When to Use

Use this skill when:
- Creating a context note as a Product Manager
- Reviewing acceptance criteria before finalizing a handoff document
- Evaluating whether AC are sufficient for QA testing

## What Makes Good Acceptance Criteria?

Good acceptance criteria are **TSIIC**:
- **T**estable
- **S**pecific
- **I**ndependent
- **I**mplementation-free
- **C**omplete

## The TSIIC Checklist

### 1. Testable
**Question:** Could a QA engineer write an automated assertion for this criterion?

**BAD:**
- "The system should be fast"
- "The UI should be user-friendly"
- "Error handling should be robust"

**GOOD:**
- "When a user submits a login form with valid credentials, the system responds within 2 seconds"
- "When a user enters an invalid email format, the system displays an inline error message 'Invalid email format'"
- "When the database connection fails, the system returns HTTP 503 with error code 'DB_UNAVAILABLE'"

### 2. Specific
**Question:** Does it reference a concrete behavior, not a vague quality?

**BAD:**
- "Users can manage their boards"
- "The system handles errors gracefully"
- "Authentication works correctly"

**GOOD:**
- "Users can create a new board by clicking the 'New Board' button, entering a board name, and clicking 'Create'"
- "When a user enters a duplicate email during registration, the system returns HTTP 409 with message 'Email already exists'"
- "When a user submits valid credentials, the system returns a JWT token with a 30-minute expiration"

### 3. Independent
**Question:** Does it describe a single verifiable outcome, not a compound requirement?

**BAD:**
- "Users can register, login, and update their profile"
- "The system validates input and stores it securely in the database"

**GOOD:**
Split into multiple criteria:
- "Users can register by providing email, username, and password"
- "Users can login with their registered credentials"
- "Users can update their profile name and avatar"

### 4. Implementation-Free
**Question:** Does it describe *what* the system does, not *how* it does it?

**BAD:**
- "The system uses BCrypt to hash passwords with a work factor of 12"
- "The API stores user data in a SQL Server database using Entity Framework Core"
- "The frontend calls the POST /api/auth/register endpoint"

**GOOD:**
- "When a user registers, the system stores their password securely (never in plain text)"
- "When a user registers, the system persists their email, username, and password"
- "When a user registers successfully, the system responds with their user ID"

### 5. Complete
**Question:** Are edge cases and failure modes covered?

**Incomplete AC (only happy path):**
- "Users can register with email and password"

**Complete AC (includes edge cases):**
- "Users can register with a valid email (format: name@domain.com) and password (minimum 8 characters)"
- "When a user attempts to register with an email that already exists, the system returns HTTP 409 with message 'Email already registered'"
- "When a user attempts to register with a password shorter than 8 characters, the system returns HTTP 400 with message 'Password must be at least 8 characters'"
- "When a user attempts to register with an invalid email format, the system returns HTTP 400 with message 'Invalid email format'"
- "When a user successfully registers, the system returns HTTP 201 with the new user's ID"

## Examples: Bad vs. Good

### Example 1: User Registration

**❌ BAD:**
- "Users should be able to sign up"

**✅ GOOD:**
- "When a user submits the registration form with valid email, username, and password (8+ chars), the system creates an account and returns HTTP 201"
- "When a user submits a registration form with an existing email, the system returns HTTP 409 with message 'Email already registered'"
- "When a user submits a registration form with a password less than 8 characters, the system returns HTTP 400 with validation error"
- "When a user submits a registration form with an invalid email format, the system returns HTTP 400 with validation error"

### Example 2: Board Creation

**❌ BAD:**
- "Users can create boards with columns"

**✅ GOOD:**
- "When a logged-in user submits a valid board name (1-100 characters), the system creates a board and returns HTTP 201 with the board ID"
- "When a user attempts to create a board without authentication, the system returns HTTP 401"
- "When a user attempts to create a board with an empty name, the system returns HTTP 400 with message 'Board name is required'"
- "When a user attempts to create a board with a name exceeding 100 characters, the system returns HTTP 400 with message 'Board name must be 100 characters or less'"

### Example 3: Password Hashing

**❌ BAD (implementation details):**
- "The system uses BCrypt with work factor 12 to hash passwords before storing them in the Users table"

**✅ GOOD (behavior, not implementation):**
- "When a user registers, the system stores their password securely (hashed, never plain text)"
- "When a user logs in with correct credentials, the system verifies the password against the stored hash"
- "When a user logs in with incorrect password, the system returns HTTP 401"

## Red Flags to Avoid

🚩 **Vague adjectives:** "quickly", "efficiently", "securely", "reliably"
🚩 **Implementation details:** Class names, method names, database tables, algorithms
🚩 **Technology choices:** "using JWT", "with BCrypt", "via Entity Framework"
🚩 **Multiple behaviors in one criterion:** "Users can create, update, and delete boards"
🚩 **Missing edge cases:** Only describing the happy path

## Template for Writing AC

Use this template structure:

```
**Feature: [Feature Name]**

**Happy Path:**
- When [user action] with [valid input], the system [expected behavior] and returns [expected response]

**Edge Cases:**
- When [user action] with [invalid input: case 1], the system returns [error response with message]
- When [user action] with [invalid input: case 2], the system returns [error response with message]

**Authorization/Security:**
- When [unauthorized user] attempts [protected action], the system returns HTTP 401/403

**Performance (if applicable):**
- When [user action] occurs, the system responds within [time constraint]
```

## Self-Review Checklist

Before finalizing AC, ask yourself:

- [ ] Can I write an automated test for each criterion?
- [ ] Does each criterion describe ONE verifiable behavior?
- [ ] Have I avoided mentioning implementation details?
- [ ] Have I covered edge cases and failure scenarios?
- [ ] Are the expected responses specific (HTTP codes, error messages)?
- [ ] Would a developer unfamiliar with the project understand what to build from these AC alone?
