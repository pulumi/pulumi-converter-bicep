open System.Diagnostics
open Converter.PulumiTypes
open Expecto
open Converter
open Converter.BicepParser

let parameterParsingTests = testList "Parsing" [
    test "basic string parameter" {
        let program = BicepParser.parseOrFail "param storageName string"
        let param = BicepProgram.findParameter "storageName" program
        Expect.equal param.name "storageName" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        Expect.equal param.defaultValue None "Default value is correct"
        Expect.equal param.decorators [ ] "There are no decorators"
        Expect.equal param.description None "There is no description"
    }

    test "basic string parameter with default value" {
        let program = BicepParser.parseOrFail "param storageName string = 'admin'"
        let param = BicepProgram.findParameter "storageName" program
        Expect.equal param.name "storageName" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        Expect.equal param.defaultValue (Some (BicepSyntax.String "admin")) "Default value is correct"
        Expect.equal param.decorators [ ] "There are no decorators"
        Expect.equal param.description None "There is no description"
    }

    test "basic int parameter with default value" {
        let program = BicepParser.parseOrFail "param count int = 42"
        let param = BicepProgram.findParameter "count" program
        Expect.equal param.name "count" "Name is correct"
        Expect.equal param.parameterType (Some "int") "Type is correct"
        Expect.equal param.defaultValue (Some (BicepSyntax.Integer 42)) "Default value is correct"
        Expect.equal param.decorators [ ] "There are no decorators"
        Expect.equal param.description None "There is no description"
    }
    
    test "basic bool parameter with default value" {
        let program = BicepParser.parseOrFail "param create bool = false"
        let param = BicepProgram.findParameter "create" program
        Expect.equal param.name "create" "Name is correct"
        Expect.equal param.parameterType (Some "bool") "Type is correct"
        Expect.equal param.defaultValue (Some (BicepSyntax.Boolean false)) "Default value is correct"
        Expect.equal param.decorators [ ] "There are no decorators"
        Expect.equal param.description None "There is no description"
    }
    
    test "description can be extracted from the parameter" {
        let program = parseOrFail """
@description('example description')
param storageAccount string
"""
        let param = BicepProgram.findParameter "storageAccount" program
        Expect.equal param.name "storageAccount" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        Expect.equal param.defaultValue None "Default value is correct"
        let expectedDecorators = [
            BicepSyntax.FunctionCall("description", [
                BicepSyntax.String "example description"
            ])
        ]
        
        Expect.equal param.decorators expectedDecorators "There are decorators"
        Expect.equal param.description (Some "example description") "description extracted correctly"
    }

    test "parameter can use current resource group location as default" {
        let program = parseOrFail "param location string = resourceGroup().location"
        let param = BicepProgram.findParameter "location" program
        Expect.equal param.name "location" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        let defaultValue = BicepSyntax.PropertyAccess(
            BicepSyntax.FunctionCall("resourceGroup", [ ]),
            "location")
        Expect.equal param.defaultValue (Some defaultValue) "Default value is correct"
    }
    
    test "parameter can use current resource group from variable location as default" {
        let program = parseOrFail """
var currentResourceGroup = resourceGroup()       
param location string = currentResourceGroup.location
"""
        let param = BicepProgram.findParameter "location" program
        Expect.equal param.name "location" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        let defaultValue = BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "currentResourceGroup", "location")
        Expect.equal param.defaultValue (Some defaultValue) "Default value is correct"
    }
    
    test "nested property access can be parsed" {
        let expr = parseExpression "first.second.third"
        let expected = BicepSyntax.PropertyAccess(
            BicepSyntax.PropertyAccess (BicepSyntax.VariableAccess "first", "second"),"third")
        Expect.equal expected expr "Nested property access is correctly parsed"
    }
    
    test "parsing basic resource works" {
        let program = parseOrFail """
param storageName string
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  name: storageName
  location: 'eastus'
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}
"""
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        Expect.equal exampleStorage.name "exampleStorage" "Name is correct"
        Expect.equal exampleStorage.token "Microsoft.Storage/storageAccounts@2021-02-01" "Token is correct"
        let expectedResourceBody = BicepSyntax.Object (Map.ofList [
            BicepSyntax.Identifier "name", BicepSyntax.VariableAccess "storageName"
            BicepSyntax.Identifier "location", BicepSyntax.String "eastus"
            BicepSyntax.Identifier "kind", BicepSyntax.String "StorageV2"
            BicepSyntax.Identifier "sku", BicepSyntax.Object (Map.ofList [
                BicepSyntax.Identifier "name", BicepSyntax.String "Standard_LRS"
            ])
        ])

        Expect.equal exampleStorage.value expectedResourceBody "Resource body is correctly parsed"
    }
    
    test "parsing conditional resource works" {
        let program = parseOrFail """
param storageName string
param createStorage bool = false
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = if (createStorage) {
  name: storageName
  location: 'eastus'
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}
"""
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        Expect.equal exampleStorage.name "exampleStorage" "Name is correct"
        Expect.equal exampleStorage.token "Microsoft.Storage/storageAccounts@2021-02-01" "Token is correct"

        let conditionalResourceBody = BicepSyntax.IfCondition (
            BicepSyntax.VariableAccess "createStorage",
            BicepSyntax.Object (Map.ofList [
                BicepSyntax.Identifier "name", BicepSyntax.VariableAccess "storageName"
                BicepSyntax.Identifier "location", BicepSyntax.String "eastus"
                BicepSyntax.Identifier "kind", BicepSyntax.String "StorageV2"
                BicepSyntax.Identifier "sku", BicepSyntax.Object (Map.ofList [
                    BicepSyntax.Identifier "name", BicepSyntax.String "Standard_LRS"
                ])
            ])
        )

        Expect.equal exampleStorage.value conditionalResourceBody "Resource body is correctly parsed"
    }
    
    test "parsing resource with for loop works" {
        let program = parseOrFail """
param networkSecurityGroupName string
param location string = resourceGroup().location
param orgNames array = ['Contoso', 'Fabrikam','Coho'] 

resource networkSecurityGroups 'Microsoft.Network/networkSecurityGroups@2020-06-01' = [for orgName in orgNames: {
  name: 'nsg-${orgName}'
  location: location
}]
"""

        let networkSecurityGroups = BicepProgram.findResource "networkSecurityGroups" program
        Expect.equal networkSecurityGroups.name "networkSecurityGroups" "Name is correct"

        let expectedForLoop = BicepSyntax.For {
            expression = BicepSyntax.VariableAccess "orgNames"
            variableSection = BicepSyntax.LocalVariable "orgName"
            body = BicepSyntax.Object(Map.ofList [
                BicepSyntax.Identifier "location", BicepSyntax.VariableAccess "location"
                BicepSyntax.Identifier "name", BicepSyntax.InterpolatedString (
                    [BicepSyntax.VariableAccess "orgName"], ["nsg-"; ""])
            ])
        }
        
        Expect.equal expectedForLoop networkSecurityGroups.value "The resource body is parsed correctly"
    }
    
    test "parsing resource with for loop using index works" {
        let program = parseOrFail """
param networkSecurityGroupName string
param location string = resourceGroup().location
param orgNames array = ['Contoso', 'Fabrikam','Coho']

resource networkSecurityGroups 'Microsoft.Network/networkSecurityGroups@2020-06-01' = [for (orgName, i) in orgNames: {
  name: 'nsg-${orgName}-${i}'
  location: location
}]
"""
        let networkSecurityGroups = BicepProgram.findResource "networkSecurityGroups" program
        Expect.equal networkSecurityGroups.name "networkSecurityGroups" "Name is correct"

        let expectedForLoop = BicepSyntax.For {
            expression = BicepSyntax.VariableAccess "orgNames"
            variableSection = BicepSyntax.LocalVariableBlock ["orgName"; "i"]
            body = BicepSyntax.Object(Map.ofList [
                BicepSyntax.Identifier "location", BicepSyntax.VariableAccess "location"
                BicepSyntax.Identifier "name", BicepSyntax.InterpolatedString (
                    [BicepSyntax.VariableAccess "orgName"; BicepSyntax.VariableAccess "i"], ["nsg-"; "-"; ""])
            ])
        }
        
        Expect.equal expectedForLoop networkSecurityGroups.value "The resource body is parsed correctly"
    }
]

let resourceTokenMappingTests = testList "Resource token mapping" [
    test "example with network security groups" {
        let azureSpecToken = "Microsoft.Network/networkSecurityGroups@2020-06-01"
        let expectedPulumiToken = "azure-native:network/v20200601:NetworkSecurityGroup"
        let token = ResourceTokens.fromAzureSpecToPulumi azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }

    test "example with storage account" {
        let azureSpecToken = "Microsoft.Storage/storageAccounts@2021-02-01"
        let expectedPulumiToken = "azure-native:storage/v20210201:StorageAccount"
        let token = ResourceTokens.fromAzureSpecToPulumi azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }
    
    test "example without version" {
        let azureSpecToken = "Microsoft.Storage/storageAccounts"
        let expectedPulumiToken = "azure-native:storage:StorageAccount"
        let token = ResourceTokens.fromAzureSpecToPulumi azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }
]

let configTypeInference = testList "Config type inference" [
    test "example with list(string)" {
        let program = parseOrFail "param names array = ['Contoso', 'Fabrikam','Coho']"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "list(string)"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }
    
    test "empty array returns list(any)" {
        let program = parseOrFail "param names array = []"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "list(any)"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }
    
    test "object with string properties becomes map(string)" {
        let program = parseOrFail "param names object = { a: 'b', c: 'd' }"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "map(string)"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }
    
    test "empty object becomes map(any)" {
        let program = parseOrFail "param names object = { }"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "map(any)"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }

    test "array of objects can be inferred" {
        let program = parseOrFail "param names array = [ { a: 'b', c: 'd' } ]"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "list(map(string))"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }
    
    test "object with properties of different types becomes an object type" {
        let program = parseOrFail "param people array = [ { name: 'John', age: 42 } ]"
        let people = BicepProgram.findParameter "people" program
        let expectedType = "list(object({age=int, name=string}))"
        let actualType = Transform.inferConfigType people
        Expect.equal actualType expectedType "Type is correctly inferred"
    }
]

let resourceTransforms = testList "resource transformations" [
    test "extracting empty resource options" {
        let properties = Map.empty
        let emptyProgram = { declarations = [ ] }
        let options = Transform.extractResourceOptions properties emptyProgram
        Expect.isNone options "resource options are None"
    }
    
    test "extracting parent from resource options" {
        let properties = Map.ofList [
            BicepSyntax.Identifier "parent", BicepSyntax.VariableAccess "rg"
        ]
        
        let emptyProgram = { declarations = [ ] }
        let options = Transform.extractResourceOptions properties emptyProgram
        Expect.isSome options "resource options are not empty"
        let options = Option.get options
        let expected = { ResourceOptions.Empty with parent = Some (PulumiSyntax.VariableAccess "rg") }
        Expect.equal options expected "parent is extracted correctly"
    }
    
    test "extracting dependsOn from resource options" {
        let properties = Map.ofList [
            BicepSyntax.Identifier "parent", BicepSyntax.VariableAccess "rg"
            BicepSyntax.Identifier "dependsOn", BicepSyntax.Array [
                BicepSyntax.VariableAccess "rg"
            ]
        ]

        let emptyProgram = { declarations = [ ] }
        let options = Transform.extractResourceOptions properties emptyProgram
        Expect.isSome options "resource options are not empty"
        let options = Option.get options
        let expected = {
            ResourceOptions.Empty with
                parent = Some (PulumiSyntax.VariableAccess "rg")
                dependsOn = Some (PulumiSyntax.Array [ PulumiSyntax.VariableAccess "rg" ])
        }
        Expect.equal options expected "parent is extracted correctly"
    }
]

let allTests = testList "All tests" [
    parameterParsingTests
    resourceTokenMappingTests
    configTypeInference
    resourceTransforms
]

[<EntryPoint>]
let main (args:string[]) = runTestsWithCLIArgs [  ] args allTests

