param storagePrefix string

param storageSKU string = 'Standard_LRS'

param location string = resourceGroup().location

var storageLocations = ['east', 'west']

resource storage 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  location: location
  kind: 'StorageV2'
  sku: {
    name: storageSKU
  }
}

resource storageAccounts 'Microsoft.Storage/storageAccounts@2022-09-01' = [for index in range (0, 10): {
  location: location
  kind: 'StorageV2'
  sku: {
    name: storageSKU
  }
}]

resource storageAccountsByLocation 'Microsoft.Storage/storageAccounts@2022-09-01' = [for location in storageLocations:{
   location: location
   kind: 'StorageV2'
   sku: {
     name: storageSKU
   }
}]

output storageEndpoint object = storage.properties.primaryEndpoints