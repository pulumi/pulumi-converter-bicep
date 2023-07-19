config resourceGroupName "string" {
    description = "The name of the resource group to operate on"
}
currentResourceGroup = invoke("azure-native:resources:getResourceGroup", {
"resourceGroupName" = resourceGroupName
})
config storagePrefix "string" {
}
config storageSKU "string" {
    default = "Standard_LRS"
}
config location "string" {
    default = currentResourceGroup.location
}
storageLocations = [
"east",
"west"
]

resource storage "azure-native:storage:StorageAccount" {
    kind = "StorageV2"
    location = location
    resourceGroupName = currentResourceGroup.name
    sku = {
    name = storageSKU
    }
}
resource storageAccounts "azure-native:storage:StorageAccount" {
    options {
        range = 10
}
    kind = "StorageV2"
    location = location
    resourceGroupName = currentResourceGroup.name
    sku = {
    name = storageSKU
    }
}
resource storageAccountsByLocation "azure-native:storage:StorageAccount" {
    options {
        range = storageLocations
}
    kind = "StorageV2"
    location = range.value
    resourceGroupName = currentResourceGroup.name
    sku = {
    name = storageSKU
    }
}
output storageEndpoint {
    value = storage.primaryEndpoints
}
