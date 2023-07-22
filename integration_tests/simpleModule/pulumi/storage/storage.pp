config resourceGroupName "string" {
    description = "The name of the resource group to operate on"
}
config location "string" {
}
resource storage "azure-native:storage:StorageAccount" {
    kind = "StorageV2"
    location = location
    resourceGroupName = resourceGroupName
    sku = {
        name = "Standard_LRS"
    }
}
exampleExistingStorage = invoke("azure-native:storage:getStorageAccount", {
    accountName = "existingStorageName"
    resourceGroupName = resourceGroupName
})
