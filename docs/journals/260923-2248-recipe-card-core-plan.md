# Technical Journal: Recipe Card Core Plan

- **Date:** 2026-09-23 22:48
- **Topic:** Architecture & MVP Scope for R&D Recipe Hub
- **Author:** Antigravity / ClaudeKit Planner

## Context

User requested clarifying the core value of the R&D Recipe Hub project defined in `doc/PRN232.SRS.md`, compared it against reference document `doc/Bộ công thức Phê La Update 13_07_2026.pdf`, and chose the tech stack and implementation path.

## Decisions Made

1. **Scope Reduction for First Deliverable:**
   - Temporarily exclude authentication, RBAC, approval workflow, document revision history, audit logs, and AI third-party generation.
   - Core vertical slice = Ingredient catalog + Recipe composition (quantities + ordered steps + photos) + Single-recipe PDF export.
   - Defer recipe variants (Size M/L, 1-3 cup batch scaling) until the single-recipe card workflow works end-to-end.

2. **Technology Stack:**
   - **Framework:** ASP.NET Core Razor Pages (.NET 10). Single project, server-rendered forms, zero separate API/SPA overhead.
   - **ORM & Database:** Entity Framework Core with local SQLite file (`recipe-card.db`). Zero DB server install required, persistent across app restarts.
   - **Asset Storage:** Local file system under `wwwroot/uploads/steps/` with GUID-based names and extension/magic-byte validation.
   - **PDF Generation:** QuestPDF using fixed Phê La-inspired operational card layout.

3. **Plan Location:**
   - Created in `plans/260923-2248-recipe-card-core/`.
   - Divided into 4 executable phases:
     - `phase-01-bootstrap-and-persistence.md`
     - `phase-02-ingredient-catalog.md`
     - `phase-03-recipe-composer.md`
     - `phase-04-preview-and-pdf-export.md`

## Next Steps

Execute implementation using the cook workflow.
