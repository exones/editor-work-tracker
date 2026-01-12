Create a commit message for staged changes using 2-step approach:

**Step 1: List changed files**
Run: `git diff --cached --name-status | Out-String`
Analyze file list to understand scope of changes.

**Step 2: Review changes (if reasonable)**

- If <20 files with small changes: run `git diff --cached | Out-String` to see full diff
- If 20-100 files: run `git diff --cached --stat | Out-String` for summary stats
- If >100 files or massive changes: use file list + stats only, infer from filenames

**PowerShell commands (all non-interactive):**

```powershell
# File list with status (M=Modified, A=Added, D=Deleted)
git diff --cached --name-status | Out-String

# Statistics summary
git diff --cached --stat | Out-String

# Full diff (for small changes only)
git diff --cached | Out-String

# Short summary
git diff --cached --shortstat | Out-String
```

**Commit message format:**

```
EVX-XXXX emoji type[(scope)]: short description

[optional body with bullet points]

[optional footers like "BREAKING CHANGE:" or "Refs: #123"]
```

- If the branch is named like EVX-XXXX, add this prefix to the commit message in the beginning: EVX-XXXX
- Use semantic commit messages.
- Description: lowercase, no period, imperative mood ("add" not "added")
- Breaking changes: add `!` after type like `feat!:` AND/OR add `BREAKING CHANGE:` footer
- Body: explain what & why (not how), use bullets for multiple changes
- Add an emoji before the prefix to make the commit message more descriptive.
- Use the following prefixes:
  - 🎉 feat: for new features
  - 🐛 fix: for bug fixes
  - ♻️ refactor: for code refactoring
  - 🧹 chore: for maintenance tasks
  - 📝 docs: for documentation changes
  - 🧪 test: for test changes
  - ⚡️ perf: for performance improvements
  - 🎨 style: for code style changes
  - 👷 build: for build system changes
  - 🔄 ci: for continuous integration changes
  - ⬆️ update: version bumps
- Detect the "main" change to use for the commit message. If there are 2-3 changes, try to combine them into one sentence, use the most significant one, and add in the end " + <number of other changes>".
- Compose an optional body as a full list of short summaries of changes
- Sacrifice grammar over conciseness! Be short, like "use fragments, drop articles, prefer 'add X' over 'adding X'", don't add articles and unnecessary grammatical constructions.
