# CommentCleaner

`CommentCleaner` is a C# CLI tool that removes comments from a single file or an entire folder.

It edits files in place, supports recursive folder processing, and can create backups before writing.

## Features

- Remove comments from one file or many files in a folder
- Recursive directory scan by default
- Skip likely binary files automatically
- Optional dry run mode
- Optional `.bak` backup creation

## Supported Comment Styles

- `// line comment`
- `/* block comment */`
- `<!-- html comment -->`
- Lua: `-- line comment` and `--[[ block ]]`
- Pascal style: `(* block comment *)`
- Start-of-line comments: `# ...` and `; ...`

## Requirements

- .NET SDK 10.0+ (`dotnet --version`)

## Usage

From inside the project folder:

```powershell
cd "C:\Users\User\Desktop\Comment\CommentCleaner"
dotnet run -- "C:\path\to\file.lua"
```

From outside the project folder:

```powershell
dotnet run --project ".\CommentCleaner\CommentCleaner.csproj" -- "C:\path\to\file.lua"
```

Process a folder:

```powershell
dotnet run --project ".\CommentCleaner\CommentCleaner.csproj" -- "C:\path\to\folder"
```

## Options

- `--dry-run`  
  Show which files would be changed, without modifying them.
- `--backup`  
  Create `filename.ext.bak` before writing changes.
- `--no-recursive`  
  Only process files in the top-level folder.
- `-h`, `--help`  
  Print help.

## Examples

Single file:

```powershell
dotnet run --project ".\CommentCleaner\CommentCleaner.csproj" -- "C:\Users\User\Desktop\Comment\input.lua"
```

Folder with backups:

```powershell
dotnet run --project ".\CommentCleaner\CommentCleaner.csproj" -- "C:\Users\User\Desktop\Comment" --backup
```

Dry run:

```powershell
dotnet run --project ".\CommentCleaner\CommentCleaner.csproj" -- "C:\Users\User\Desktop\Comment" --dry-run
```

## Build a Standalone Executable

```powershell
cd "C:\Users\User\Desktop\Comment\CommentCleaner"
dotnet publish -c Release
```

Published output:

`bin\Release\net10.0\publish\CommentCleaner.exe`

Run it directly:

```powershell
.\bin\Release\net10.0\publish\CommentCleaner.exe "C:\path\to\folder" --backup
```

## Notes

- Files are modified in place, so use `--backup` for safety.
- Comment stripping is syntax-heuristic based, not a full parser for every language.
- The tool preserves strings and removes trailing spaces caused by removed comments.
