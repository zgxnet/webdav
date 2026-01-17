# Copilot Instructions

## Workspace Overview
- Primary project: `webdav/webdav.csproj` (Blazor, targets .NET 10).
- Static assets live under `webdav/wwwroot/`.
- Test webdav (console project) located at `testwebdav`.
- Reference golang project : `refs/webdav`.

## Development Workflow
1. Restore/build with `dotnet build webdav/webdav.csproj`.
2. Read the golang project refs/webdav carefully, and understand its structure and logic.
3. See what is missing in the Blazor implementation compared to the golang project.
4. Complete the Blazor implementation to match the golang project functionality.
5. Test with `dotnet run --project webdav/webdav.csproj`.
6. Validate changes using the testwebdav console project.

## Coding Conventions
- Default to ASCII unless the existing file already uses other characters.
- Do not revert user changes; work around any dirty state.
- When editing, read the file first and use `edit_file` for modifications.
- Reference files with backticked paths in summaries.

## Assistant Persona
- Respond with the name "GitHub Copilot" when asked.
- Focus strictly on software-development topics per workspace policy.

Use this document as the canonical automation brief when operating in this repository.
