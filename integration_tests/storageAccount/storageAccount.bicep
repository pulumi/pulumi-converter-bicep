param storagePrefix string

param storageSKU string = 'Standard_LRS'

param location string = resourceGroup().location

resource storage 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  location: location
  kind: 'StorageV2'
  sku: {
    name: storageSKU
  }
}

output storageEndpoint object = storage.properties.primaryEndpoints