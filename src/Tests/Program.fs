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
    
    test "parsing module declaration should work" {
        let program = parseOrFail """
module storageModule '../storageAccount.bicep' = {
  name: 'storageDeploy'
  params: {
    storagePrefix: 'examplestg1'
  }
}
"""

        let storageModule = BicepProgram.findModule "storageModule" program
        Expect.equal storageModule.path "../storageAccount.bicep" "path is parsed correctly"
        let expectedBody = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "name", BicepSyntax.String "storageDeploy"
            BicepSyntax.Identifier "params", BicepSyntax.Object(Map.ofList [
                BicepSyntax.Identifier "storagePrefix", BicepSyntax.String "examplestg1"
            ])
        ])
        
        Expect.equal storageModule.value expectedBody "module body is parsed correctly"
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
    
    test "full program conversion" {
        let program = parseOrFail """
@description('name of the new virtual network where DNS resolver will be created')
param resolverVNETName string = 'dnsresolverVNET'

@description('the IP address space for the resolver virtual network')
param resolverVNETAddressSpace string = '10.7.0.0/24'

@description('name of the dns private resolver')
param dnsResolverName string = 'dnsResolver'

@description('the location for resolver VNET and dns private resolver - Azure DNS Private Resolver available in specific region, refer the documenation to select the supported region for this deployment. For more information https://docs.microsoft.com/azure/dns/dns-private-resolver-overview#regional-availability')
param location string

@description('name of the subnet that will be used for private resolver inbound endpoint')
param inboundSubnet string = 'snet-inbound'

@description('the inbound endpoint subnet address space')
param inboundAddressPrefix string = '10.7.0.0/28'

@description('name of the subnet that will be used for private resolver outbound endpoint')
param outboundSubnet string = 'snet-outbound'

@description('the outbound endpoint subnet address space')
param outboundAddressPrefix string = '10.7.0.16/28'

@description('name of the vnet link that links outbound endpoint with forwarding rule set')
param resolvervnetlink string = 'vnetlink'

@description('name of the forwarding ruleset')
param forwardingRulesetName string = 'forwardingRule'

@description('name of the forwarding rule name')
param forwardingRuleName string = 'contosocom'

@description('the target domain name for the forwarding ruleset')
param DomainName string = 'contoso.com.'

@description('the list of target DNS servers ip address and the port number for conditional forwarding')
param targetDNS array = [
  {
    ipaddress: '10.0.0.4'
    port: 53
  }
  {
    ipaddress: '10.0.0.5'
    port: 53
  }
]

resource resolver 'Microsoft.Network/dnsResolvers@2022-07-01' = {
  name: dnsResolverName
  location: location
  properties: {
    virtualNetwork: {
      id: resolverVnet.id
    }
  }
}

resource inEndpoint 'Microsoft.Network/dnsResolvers/inboundEndpoints@2022-07-01' = {
  parent: resolver
  name: inboundSubnet
  location: location
  properties: {
    ipConfigurations: [
      {
        privateIpAllocationMethod: 'Dynamic'
        subnet: {
          id: '${resolverVnet.id}/subnets/${inboundSubnet}'
        }
      }
    ]
  }
}

resource outEndpoint 'Microsoft.Network/dnsResolvers/outboundEndpoints@2022-07-01' = {
  parent: resolver
  name: outboundSubnet
  location: location
  properties: {
    subnet: {
      id: '${resolverVnet.id}/subnets/${outboundSubnet}'
    }
  }
}

resource fwruleSet 'Microsoft.Network/dnsForwardingRulesets@2022-07-01' = {
  name: forwardingRulesetName
  location: location
  properties: {
    dnsResolverOutboundEndpoints: [
      {
        id: outEndpoint.id
      }
    ]
  }
}

resource resolverLink 'Microsoft.Network/dnsForwardingRulesets/virtualNetworkLinks@2022-07-01' = {
  parent: fwruleSet
  name: resolvervnetlink
  properties: {
    virtualNetwork: {
      id: resolverVnet.id
    }
  }
}

resource fwRules 'Microsoft.Network/dnsForwardingRulesets/forwardingRules@2022-07-01' = {
  parent: fwruleSet
  name: forwardingRuleName
  properties: {
    domainName: DomainName
    targetDnsServers: targetDNS
  }
}

resource resolverVnet 'Microsoft.Network/virtualNetworks@2022-01-01' = {
  name: resolverVNETName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        resolverVNETAddressSpace
      ]
    }
    enableDdosProtection: false
    enableVmProtection: false
    subnets: [
      {
        name: inboundSubnet
        properties: {
          addressPrefix: inboundAddressPrefix
          delegations: [
            {
              name: 'Microsoft.Network.dnsResolvers'
              properties: {
                serviceName: 'Microsoft.Network/dnsResolvers'
              }
            }
          ]
        }
      }
      {
        name: outboundSubnet
        properties: {
          addressPrefix: outboundAddressPrefix
          delegations: [
            {
              name: 'Microsoft.Network.dnsResolvers'
              properties: {
                serviceName: 'Microsoft.Network/dnsResolvers'
              }
            }
          ]
        }
      }
    ]
  }
}
"""
        let pulumiProgram = Transform.bicepProgram program
        let programText = Printer.printProgram pulumiProgram
        Expect.isNotEmpty programText "there is text"
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
    
    test "simple resource can be transformed"  {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  name: 'storage'
  location: 'eastus'
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}
"""
        
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            Expect.equal pulumiResource.name "exampleStorage" "The name is correct"
            Expect.equal pulumiResource.token "azure-native:storage/v20210201:StorageAccount" "token is correct"
            let expectedResourceInputs = Map.ofList [
                "location", PulumiSyntax.String "eastus"
                "kind", PulumiSyntax.String "StorageV2"
                "sku", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "name", PulumiSyntax.String "Standard_LRS"
                ])
            ]
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
            Expect.isNone pulumiResource.options "There were no resource options"
            Expect.equal pulumiResource.logicalName (Some "storage") "logical name is extracted correctly"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }
    
    test "resource input odata.type is normalized to odataType"  {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  name: 'storage'
  'odata.type': 'eastus'
  sku: {
    'odata.type': 'Standard_LRS'
  }
}
"""

        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            let expectedResourceInputs = Map.ofList [
                "odataType", PulumiSyntax.String "eastus"
                "sku", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "odataType", PulumiSyntax.String "Standard_LRS"
                ])
            ]
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }
    
    test "conditional resource can be transformed" {
        let program = parseOrFail """
param createStorage bool = false
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = if (createStorage) {
  name: 'storage'
  location: 'eastus'
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}
"""
        
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            Expect.equal pulumiResource.name "exampleStorage" "The name is correct"
            Expect.equal pulumiResource.token "azure-native:storage/v20210201:StorageAccount" "token is correct"
            let expectedResourceInputs = Map.ofList [
                "location", PulumiSyntax.String "eastus"
                "kind", PulumiSyntax.String "StorageV2"
                "sku", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "name", PulumiSyntax.String "Standard_LRS"
                ])
            ]
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
            match pulumiResource.options with
            | None -> failwith "expected resource options to have a value"
            | Some resourceOptions ->
                let expectedOptions = {
                    ResourceOptions.Empty
                        with range = Some (PulumiSyntax.TernaryExpression(
                            PulumiSyntax.VariableAccess "createStorage",
                            PulumiSyntax.Integer 1,
                            PulumiSyntax.Integer 0))
                }

                Expect.equal resourceOptions expectedOptions "resource options are parsed correctly"
            Expect.equal pulumiResource.logicalName (Some "storage") "logical name is extracted correctly"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }
    
    test "transforming module to component declaration works" {
        let program = parseOrFail """
module storageModule '../storageAccount.bicep' = {
  name: 'storageDeploy'
  params: {
    storagePrefix: 'examplestg1'
  }
}
"""
        let storageModule = BicepProgram.findModule "storageModule" program
        match Transform.bicepModule storageModule program with
        | None -> failwith "failed to transform storageModule"
        | Some componentDecl ->
            Expect.equal componentDecl.name "storageModule" "Name is correct"
            Expect.equal componentDecl.path "../storageAccount.bicep" "path is correct"
            Expect.equal componentDecl.logicalName (Some "storageDeploy") "logical name is correct"
            let expectedInputs = Map.ofList [
                PulumiSyntax.Identifier "storagePrefix", PulumiSyntax.String "examplestg1"
            ]
            Expect.equal componentDecl.inputs expectedInputs "inputs are correct"
    }
    
    test "resource with range(0, x) can be transformed into a simple count" {
        let program = parseOrFail """
param locations array = ['eastus', 'westus']
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = [for index in range(0, length(locations)) : { 
  name: 'storage'
  location: locations[index]
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}]
"""
        
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            Expect.equal pulumiResource.name "exampleStorage" "The name is correct"
            Expect.equal pulumiResource.token "azure-native:storage/v20210201:StorageAccount" "token is correct"
            let expectedResourceInputs = Map.ofList [
                // location = locations[index] is converted to location = locations[range.value]
                "location", PulumiSyntax.IndexExpression(
                    PulumiSyntax.VariableAccess "locations",
                    PulumiSyntax.PropertyAccess (PulumiSyntax.VariableAccess "range", "value"))
                
                "kind", PulumiSyntax.String "StorageV2"
                "sku", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "name", PulumiSyntax.String "Standard_LRS"
                ])
            ]
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
            match pulumiResource.options with
            | None -> failwith "expected resource options to have a value"
            | Some resourceOptions ->
                let expectedOptions = {
                    ResourceOptions.Empty
                        with range = Some (
                            PulumiSyntax.FunctionCall("length", [ PulumiSyntax.VariableAccess "locations"  ])
                        )
                }

                Expect.equal resourceOptions expectedOptions "resource options are parsed correctly"
            Expect.equal pulumiResource.logicalName (Some "storage") "logical name is extracted correctly"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }
    
    test "resource iterating over generic collection can be transformed into range" {
        let program = parseOrFail """
param locations array = ['eastus', 'westus']
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = [for (location, index) in locations : { 
  location: locations[index]
  tag: location
}]
"""

        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            Expect.equal pulumiResource.name "exampleStorage" "The name is correct"
            Expect.equal pulumiResource.token "azure-native:storage/v20210201:StorageAccount" "token is correct"
            let expectedResourceInputs = Map.ofList [
                // location = locations[index] is converted to location = locations[range.key]
                "location", PulumiSyntax.IndexExpression(
                    PulumiSyntax.VariableAccess "locations",
                    PulumiSyntax.PropertyAccess (PulumiSyntax.VariableAccess "range", "key"))
                
                // location reference becomes range.value
                "tag", PulumiSyntax.PropertyAccess (PulumiSyntax.VariableAccess "range", "value")
            ]
            
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
            match pulumiResource.options with
            | None -> failwith "expected resource options to have a value"
            | Some resourceOptions ->
                let expectedOptions = {
                    ResourceOptions.Empty with range = Some (PulumiSyntax.VariableAccess "locations")
                }

                Expect.equal resourceOptions expectedOptions "resource options are parsed correctly"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
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

