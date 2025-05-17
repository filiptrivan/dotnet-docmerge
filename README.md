# DotNetDocMerge

A .NET global tool that generates a single HTML documentation file from XML documentation summaries in C# files.

## Video Usage Example

https://youtu.be/NnHIXMs4BPI?si=NCk0NdRwvOKnA0-d

## Features

- Recursively searches for all C# files in the current directory
- Extracts XML documentation comments (summaries)
- Generates a clean, formatted HTML file with:
  - Table of contents
  - File-based sections with properly formatted titles
  - Clean presentation of documentation comments
- Maintains file paths for reference

## Installation

To install the tool globally on your system:

```bash
dotnet pack
dotnet tool install --global --add-source ./nupkg DotNetDocMerge
```

## Usage

Navigate to any directory containing C# files and run:

```bash
docmerge
```

This will:
1. Search for all C# files in the current directory and subdirectories
2. Extract documentation comments
3. Generate a `documentation.html` file in the current directory

## Output

The tool generates a single `documentation.html` file that includes:
- A table of contents with links to each file's documentation
- Sections for each file with:
  - A formatted title (capitalized and spaced)
  - The relative file path
  - All documentation summaries found in the file

## Requirements

- .NET 9.0 SDK or later

## Uninstallation

To remove the tool from your system:

```bash
dotnet tool uninstall -g DotNetDocMerge
``` 