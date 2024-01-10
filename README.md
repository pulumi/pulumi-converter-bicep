# pulumi-converter-bicep

A Pulumi converter plugin to convert Bicep code to Pulumi languages. Currently work in progress.

### Installation
Install the plugin from Github releases using the following command
```
pulumi plugin install converter bicep
```

### Usage
Run the following command in the directory where your Bicep files are located
```
pulumi convert --from bicep --language <language> --out pulumi -- --entry <entry-file>
```
Will convert Bicep code into your language of choice: `typescript`, `csharp`, `python`, `go`, `java` or `yaml`

> Note that if you don't specify the `--entry` flag, the converter will look for the first file in the current directory which has the `.bicep` extension.

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
    accountName: "storageaccount",
    kind: "StorageV2",
    location: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.location),
    resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
    sku: {
        name: "Standard_LRS",
    },
});

```
or to `csharp`
```csharp
using System.Collections.Generic;
using System.Linq;
using Pulumi;
using AzureNative = Pulumi.AzureNative;

return await Deployment.RunAsync(() => 
{
    var config = new Config();
    // The name of the resource group to operate on
    var resourceGroupName = config.Require("resourceGroupName");
    var currentResourceGroup = AzureNative.Resources.GetResourceGroup.Invoke(new()
    {
        ResourceGroupName = resourceGroupName,
    });

    var storage = new AzureNative.Storage.StorageAccount("storageaccount", new()
    {
        AccountName = "storageaccount",
        Kind = "StorageV2",
        Location = currentResourceGroup.Apply(getResourceGroupResult => getResourceGroupResult.Location),
        ResourceGroupName = currentResourceGroup.Apply(getResourceGroupResult => getResourceGroupResult.Name),
        Sku = new AzureNative.Storage.Inputs.SkuArgs
        {
            Name = "Standard_LRS",
        },
    });

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