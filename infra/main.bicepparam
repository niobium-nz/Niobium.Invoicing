using 'main.bicep'

var targetEnvironment = readEnvironmentVariable('AZURE_ENV_NAME', 'dev')

param environmentName = targetEnvironment
param notificationReceiptResourceGroup = targetEnvironment == 'test' || targetEnvironment == 'prod' ? 'rg-niobium-notify-${targetEnvironment}' : ''
param appShortName = readEnvironmentVariable('APP_SHORT_NAME', 'niobiuminvoicing')
param customDomainName = readEnvironmentVariable('CUSTOM_DOMAIN_NAME', '')
param serviceBusQueueNames = readEnvironmentVariable('SERVICE_BUS_QUEUE_NAMES', 'issueinvoicecommand')
param storageTableNames = readEnvironmentVariable('STORAGE_TABLE_NAMES', 'Invoice,InvoiceItem,Billee,Billable,InvoiceRevision,InvoiceRevisionPart,InvoicePayment,InvoicePaymentPosting,InvoicePaymentIndex,InvoiceNotificationDelivery')
param isInteractiveDeployer = bool(readEnvironmentVariable('IS_INTERACTIVE_DEPLOYER', 'true'))
