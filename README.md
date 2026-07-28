# HamsterHub

HamsterHub is an ASP.NET Core MVC application for building healthy family
pet-care habits.

## Build and test

Install the .NET 10 SDK, then run:

```powershell
dotnet restore .\HamsterHub.slnx
dotnet build .\HamsterHub.slnx --no-restore
dotnet test .\HamsterHub.slnx --configuration Release --no-restore
```

To collect Cobertura coverage under `TestResults`, run:

```powershell
dotnet test .\HamsterHub.slnx --configuration Release --no-restore --results-directory .\TestResults --coverlet --coverlet-output-format cobertura
```

The development app applies committed Entity Framework migrations to the local
`HamsterHub` LocalDB automatically at startup. They can also be applied
explicitly with:

```powershell
dotnet ef database update --project .\HamsterHub\HamsterHub.csproj
```
