# Azure Media Services Migration Tool
![.net](https://img.shields.io/badge/Frameworks-net6-purple)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet version (MediaMigrate)](https://img.shields.io/nuget/v/MediaMigrate.svg)](https://www.nuget.org/packages/MediaMigrate/)
## Overview

**A command line tool to migrate your media data. Primarily deisgned to migrate from Azure Media Services.**
### **[Azure Media Services](https://azure.microsoft.com/en-us/products/media-services/)** is being **[retired](https://learn.microsoft.com/en-us/azure/media-services/latest/azure-media-services-retirement)**.
If you have been using the service for your streaming needs and want to move on you have reached the right place.

Presenting a rich and flexible command line tool to migrate your data from Azure Media Services.
This tool helps you to migrate your media data from Azure Media Services (AMS). It can be used to just copy the data to other cloud storage like AWS S3 or GCS.
Or can be packaged to be streamed directly from Azure storage or any web server.

It has extensible support to migrate AMS assets to Azure storage or AWS S3 or GCS. 
Fairly easy to plugin another service (e.g mux.com or encoding.com) or another cloud storage if needed.

The tool supports both [shaka-packager](https://github.com/shaka-project/shaka-packager) and [ffmpeg](https://ffmpeg.org/) to conver the videos to directly streamable format.
The content is converted to CMAF format with both a DASH and HLS manifest to support a wide range of devices.
The default is shaka packager because it can use pipes to reduce the temporary storage required but can changed via the command line.

## Features
* Cross-Platform. Works on all platforms where .NET core is available.
* Simple command line interface. Intuitive and easy to use.
* Docker container to run anywhere or can be installed as a .NET tool.
* Support for packaging both VOD and live archive assets.
* Marks migrated assets and provides summary.

## Content Migration

The tool supports various forms of content migration depending on the asset format and the command line options.
* It can simply copy and upload the files to the new storage account.
* For direct streaming, it can convert the assets to CMAF files with a DASH and HLS manifest.
* It can convert files to MP4 format and then upload for progressive download.

### Supported Asset Formats.
The below table summarizes the supported asset formats.

| Format | Copy | Packaging (DASH/HLS) |
|--------|------|----------------------|
| Single MP4 |  Yes | No |
| Multiple MP4 without .ism | Yes | No
| Multiple MP4 with .ism | Yes | Yes |
| Smooth streaming asset | No | Yes |
| Statically packaged HLS | No | Yes |
| Asset with text tracks | Yes | Yes |
| Storage encrypted assets | Yes | No |
| Live archive | No | Yes |
| Low-latency Live archive | No | Yes |
| Encrypted Live archive | No | No |
| Live archive with captions | No | Yes |
| Live archive with discontinuities | No | No (discontinuities not handled)|

## Open Issues

* Support to migrate AMS transforms to AWS Elemental Media Convert Job Specification.
* Support to convert AMS transforms to GCS Trancsode API Job templates.
* Support to migrate AMS Keys to AWS KMS or GCP Secret Manager.

# How to Run

## Local

* Install .NET SDK for the platform you are running. Click [here](https://dotnet.microsoft.com/en-us/download) on how to download.
* Run the following command to install the tool
```bash
dotnet tool install -g mediamigrate
```
* Run the tool with -? to get more help. You may have to add the install location to PATH in some instances
```bash
mediamigrate -?
```
Typical command is of the form
```bash
mediamigrate command -s subscriptionid -g resourcegroup -n amsaccount [options...]
```

## Running the tool in the Cloud.
The tool is packaged as a docker container and is available to run in the cloud.

```bash
docker pull ghcr.io/duggaraju/mediamigrate:main
```
## Azure
If you want to run the migration tool in the azure cloud you can use Azure Container Instances or Azure Functions
The example below uses ACI with user assigned managed identity that has access to the storage/media account as needed.
Please refer to [ACI](https://learn.microsoft.com/en-us/cli/azure/container?view=azure-cli-latest#az-container-create) for more details on container creation.
```bash
az container create --resource-group group --name mediamigrate --image ghcr.io/duggaraju/mediamigrate:main --assign-dentity /subscription/subcriptionid/resoruceGroups/group/providers/Microsoft.ManagedIdentity/userAssignedIdentities/myID --command-line "analyze -s subscription -g resourcegrup -n account" --restart-policy Never
```
# Credentials and Privileges
The tools uses Azure Identity library for authentication.
See [here](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme?view=azure-dotnet) for various ways to authenticate and the settings needed.
The identity used to migrate must have 
* 'Media Services Media Operator' role on the Azure Media Services account being migrated.
* 'Contributor' role on the Azure Media Services account if you have storage encrypted assets and need acces to key to decrypt them.
* 'Storage Blob Data Contributor' role on the Storage accounts used (source/destination)

You can run the following az command line to give the privileges
```bash
az role assignment create --assignee sp_name_or_managed_identity --role "Media Services Media Operator" --scope /subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/MyResourceGroup/providers/Microsoft.Media/mediaServices/mediaaccount
az role assignment create --assignee sp_name_or_managed_identity --role "Storage Blob Data Contributor" --scope /subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/MyResourceGroup/providers/Microsoft.Storage/storageAccounts/storageaccount
```

# Temporary storage needed.
The tool uses temporary storage space for format conversion and uses pipes where possible to minimize storage usage.
Smooth Streaming assets or assets from live events dont need to be downloaded locally.

## Linux
* The only storage needed is for manifests when using shaka packager.
* When using ffmpeg, if the asset files are MP4, it downloads the files locally before converting so storage is proportional to asset size.

## Windows
* Shaka packager writes the packaged files to local disk first before uploading due to a windows specific bug.
* Using ffmpeg needs doulbe the local disk space when packaging MP4 files.
* Smooth Streaming assets or assets from live events dont need to be downloaded locally.

# Destination Cloud for migration.
You can migrate your data to various cloud services like AWS or GCP or keep within Azure by moving to a storage account.

## Migrate to an Azure Storage Account.

Ensure that the Identity you are using to migrate has the following permissions
* 'Storage Blob Data Contributor' role on the storage account to which you are migrating.

## Migrate to an AWS S3 account.

* If Running locally
    * Create a bucket (or use an existing bucket) in your S3 account.
    * Create an API access key and secret.
    * Create a profile with those settings.
    * Set the environment variable AWS_PROFILE to the profile name used in step above.
    * Run the tool.
* Running in the cloud.
    * TBD
## Migrate to a GCS account.
To migrate to a Google cloud storage bucket

* If running locally
    * Create or use an existing bucket in the region you want to migrate.
    * Install gcloud CLI https://cloud.google.com/sdk/docs/install#installation_instructions
    * If running locally install google cloud shell and run 
   ```
    gcloud auth application-default login
   ```
    * Run the command by specifying the bucket name as part of the path template. e.g -t bucket_name/{AssetId}
* If running a container in the cloud
    * Ensure the bucket to use is alread created.
    * Create API keys and pass them as environment variables to the container

# Migrate to a custom cloud/service.
If your want to migrate to a service or cloud other than the ones supported out of box, you can write your own custom migrator if needed

* Clone the source code locally.
* Implement your custom migration. Look in to aws/gcp folder for examples.
* Build and Run the code locally.

# FFmpeg dependency
The tool optionally uses ffmpeg for media format conversion. It primarily uses shaka-packager but can be changed to use ffmpeg.
It doesn't ship a copy of FFmpeg itself but uses the one in the PATH.
* On Windows you can use winget or chocolatey to install ffmpeg.
```
winget install ffmpeg
```
* On Ubuntu/Debian Linux use apt to install ffmpeg
```
sudo apt install -y ffmpeg
```
* On RedHat Linux use dnf to install ffmpeg.
```
sudo dnf install ffmpeg
```
* On MacOs use brew to install ffmpeg
```
brew install ffmpeg
```
