# pulumi-converter-bicep

A Pulumi converter plugin to convert Bicep files to Pulumi languages. Currently work in progress.

### Installation

```
pulumi plugin install converter bicep <release-version> --server github://api.github.com/Zaid-Ajaj
```

### Usage
In a directory with a single Bicep file, run the following command:
```
pulumi convert --from bicep --language <language> --out pulumi
```
Will convert Bicep code into your language of choice: `typescript`, `csharp`, `python`, `go`, `java` or `yaml`

## Development

The following commands are available which you can run inside the root directory of the repo.

### Build the solution

```bash
dotnet run build 
```

### Run unit tests
```bash
dotnet run tests
```

### Run integration tests
```bash
dotnet run integration-tests
```