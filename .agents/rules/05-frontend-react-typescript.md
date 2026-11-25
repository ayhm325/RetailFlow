---
trigger: glob
globs: "**/*.tsx, **/*.ts, **/*.jsx, **/*.js, **/*.css, **/*.scss"
description: Apply when changing React, TypeScript, browser authentication, client routing, state, accessibility, or frontend tests.
---
# Frontend
- Inspect actual client app presence, package manager, lockfile, framework, and existing patterns before assuming a frontend exists.
- Treat API contracts as the boundary; do not duplicate business authorization or tenant security in the browser.
- Avoid storing bearer credentials/session secrets in localStorage/sessionStorage unless an accepted security decision explicitly requires and justifies it.
- Handle loading, empty, validation, error, unauthorized, forbidden, and retry states.
- Use accessible semantics, keyboard behavior, responsive layouts, and focused component tests.
- Do not create a frontend project or install dependencies unless the task explicitly authorizes it.
