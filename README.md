# MiViA Desktop Application End-User Documentation

## Introduction

MiViA Desktop Application is a tool designed to facilitate the analysis of microscopic images of microstructures obtained from light microscopes. The application seamlessly connects to the [MiViA system](https://app.mivia.ai), allowing users to upload and analyze their images with minimal effort. The MiViA Desktop Application can automatically upload files from a selected directory on your computer. 

## System Requirements

To run the MiViA Desktop Application, your system must have .NET 7 installed. If you do not have .NET 7, please download and install it from the [Official .NET Core Page](https://dotnet.microsoft.com/en-us/download/dotnet/7.0).

## Installation

1. Download the MiViA Desktop Application ZIP file from the [official website](https://mivia.ai/company/download) or Github Releases.
2. Locate the downloaded ZIP file on your computer and extract its contents to a convenient location.
3. Navigate to the extracted files and locate the executable file (with .exe extension).
4. Double-click on the executable file to run the MiViA Desktop Application.

## Getting Started

### Obtaining an Access Key

Before you can use the MiViA Desktop Application, you will need an Access Key. This key is directly connected to the license assigned to your account, and you are responsible for its safety. Do not share your Access Key.

To obtain an Access Key:

1. Log in to your account on the MiViA website (app.mivia.ai).
2. Select "Access Key" from the User menu.
3. Click on "Generate New Key". Your new Access Key will be displayed.

### Initial setup

Upon first run of the MiViA Desktop Application, you will need to enter your Access Key, select a directory for automatic file upload, and choose the desired models for analysis.

1. Open the MiViA Desktop Application.
2. Enter your Access Key in the designated field.
3. Click on the "Browse" button and select the directory you want the application to monitor for automatic file uploads.
4. Select the desired models for analysis from the drop-down menu.
5. Click on the "Save" button to store these settings. They will be used for subsequent runs of the application.

The MiViA Desktop Application will now run in the background. You can close the main window, but an icon will remain in your system tray (next to the system clock). You can restore the settings window or access other options by right-clicking on this icon and selecting from the context menu.

## Using the Application

The MiViA Desktop Application operates in the background, automatically uploading and analyzing any new images added to the selected directory.

### Notifications and Reports

Any errors encountered during the operation of the application will be displayed as Windows notifications. Successful analyses will produce a PDF report for each image. The report file will be named the same as the source image, with the addition of the model name as a suffix.

## Troubleshooting

### Windows Security SmartScreen

When you run the MiViA Desktop Application for the first time, Windows Security SmartScreen may display a blue screen with a warning message. This is a normal security measure and can be ignored for this application.

To proceed:

1. Click on the "More info" link on the SmartScreen window.
2. Click on the "Run anyway" button.
3. The MiViA Desktop Application will now start.

## Version History

Version 1.0.0 (18.07.2023) - Initial release


## Contribiuting

We invite you to contribute to the MiViA Desktop Application's ongoing development. Your input and ideas are important to us and can help make this tool even better for everyone. Your participation is highly valued and can make a significant impact!
