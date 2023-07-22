# pulumi-converter-bicep

A Pulumi converter plugin to convert Bicep files to Pulumi languages. Currently work in progress.

### Installation

```
pulumi plugin install converter bicep --server github://api.github.com/Zaid-Ajaj
```

### Usage
In a directory with a single Bicep file, run the following command:
```
pulumi convert --from bicep --language <language> --out pulumi
```
Will convert Bicep code into your language of choice: `typescript`, `csharp`, `python`, `go`, `java` or `yaml`

### Example
```bicep
resource storage 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: 'storageaccount'
  location: resourceGroup().location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}
```
Converts to `typescript`
```typescript
import * as pulumi from "@pulumi/pulumi";
import * as azure_native from "@pulumi/azure-native";

const config = new pulumi.Config();
// The name of the resource group to operate on
const resourceGroupName = config.require("resourceGroupName");
const currentResourceGroup = azure_native.resources.getResourceGroupOutput({
    resourceGroupName: resourceGroupName,
});
const storage = new azure_native.storage.StorageAccount("storageaccount", {
    kind: "StorageV2",
    location: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.location),
    resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
    sku: {
        name: "Standard_LRS",
    },
});

```

### Development

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