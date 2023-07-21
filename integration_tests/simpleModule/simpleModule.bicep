module main './storage.bicep' = {
    name: 'storageModule'
    params: {
        location: resourceGroup().location
    }
}