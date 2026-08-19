targetScope = 'resourceGroup'

@minLength(3)
@maxLength(4)
@description('Name of the environment.')
param environmentName string

@description('Short name used as a prefix for Azure resources. Keep it globally unique where required.')
param appShortName string

@description('App settings to project into the container app environment.')
param appSettings array = []

@description('Automatically set by azd. True if the app already exists.')
param appExists bool = false

@description('Custom domain name bind to the container app.')
param customDomainName string = ''

@description('Name of the Queues, separated by comma.')
param serviceBusQueueNames string = ''

@description('Id of the user identity to be used for testing and debugging. This is not required in production. Leave empty if not needed. Can optionally use deployer().objectId if manually deployed')
param userIdentityPrincipalId string = ''

var abbrs = loadJsonContent('./abbreviations.json')

var serviceBusNamespaceName = '${appShortName}-${abbrs.serviceBusNamespaces}${environmentName}'
module serviceBus 'service-bus.bicep' = {
  params: {
    serviceBusNamespaceName: serviceBusNamespaceName
    serviceBusQueueNames: empty(serviceBusQueueNames) ? [] : split(serviceBusQueueNames, ',')
  }
}
var serviceBusSettings = [ 
    { 
        name: 'AzureWebJobsServiceBus__fullyQualifiedNamespace'
        value: serviceBus.outputs.fullyQualifiedNamespace
    }
    { 
        name: 'ServiceBusTriggerOptions__FullyQualifiedNamespace'
        value: serviceBus.outputs.fullyQualifiedNamespace
    }
    { 
        name: 'ServiceBusOptions__FullyQualifiedNamespace'
        value: serviceBus.outputs.fullyQualifiedNamespace
    }
]

var storageAccountName = replace('${appShortName}-${abbrs.storageStorageAccounts}d${environmentName}', '-', '')
module storageAccount 'br/public:avm/res/storage/storage-account:0.32.0' = {
  params: {
    name: storageAccountName
    skuName: 'Standard_LRS'
    kind: 'StorageV2'
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}
var storageSettings = [ 
    { 
        name: 'AzureWebJobsStorage__blobServiceUri'
        value: storageAccount.outputs.serviceEndpoints.blob
    }
    { 
        name: 'AzureWebJobsStorage__tableServiceUri'
        value: storageAccount.outputs.serviceEndpoints.table
    }
    { 
        name: 'StorageTableOptions__FullyQualifiedDomainName'
        value: storageAccount.outputs.serviceEndpoints.table
    }
]

module app 'function-app.bicep' = {
  params: {
    appShortName: appShortName
    environmentName: environmentName
    appSettings: concat(appSettings, serviceBusSettings, storageSettings)
    customDomainName: customDomainName
    userIdentityPrincipalId: userIdentityPrincipalId
  }
}

module rbac 'rbac.bicep' = {
  params: {
    userIdentityPrincipalId: userIdentityPrincipalId
    managedIdentityPrincipalId: app.outputs.managedIdentityPrincipalId
    storageAccountNames: [storageAccountName]
    serviceBusNamespaceNames: [serviceBusNamespaceName]
  }
}