using './deployment.bicep'

// The media serivces account being migrated.
param mediaAccountName = 'nimbuspm'
param mediaAccountRG = 'johndeu_DO_NOT_DELETE'

// If media account is in a different subscrtipion than where the migration is running.
param mediaAccountSubscription = '2b461b25-f7b4-4a22-90cc-d640a14b5471'

// The storage account where migrated data is written.
param storageAccountName = 'amsencodermsitest'
param storageAccountRG = 'amsmediacore'

// If the storage account is in a different subscription than where the tool is running.
// param storageAccountSubscription = ''

// setting to turn encryption on or off.
param encrypt = false

// The key vault to store encryption keys if encryption is turned on.
param keyvaultname = 'mpprovenance'
param keyvaultRG = 'provenance'

//additional arguments.
param arguments = [
  '-d'
  '-t'
  '$web/johndeu/\${AssetName}'
]
