# Reference generator

`generate.py` rewrites the API tables of `docs/client-reference.md`, `docs/contracts-reference.md` and `docs/server-reference.md` from the XML comments of the built assemblies.

1. `dotnet build` (the projects write `SystemX.Auth2.*.xml` next to the dll).
2. `python3 tools/docs/generate.py` (optional first argument: the path of `SystemX.Auth2.Server.dll`).

`ListPublic.cs` is a file-based C# program (`dotnet run ListPublic.cs -- <dll> <folders>`). It prints the doc ids of the public types and members, so internal types with comments are not listed. Only the text between `<!-- generated:start -->` and `<!-- generated:end -->` changes.
