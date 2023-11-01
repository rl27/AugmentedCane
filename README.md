# AugmentedCane
A smartphone-based navigation system for people with blindness or visual impairment.

***

### GPS Navigation & TTS

To use GPS navigation and TTS, you will need a Google Cloud API key. You will need to enable the Cloud Text-to-Speech API and the Routes API. Place your API key in a file named `apikey.txt` in Assets/StreamingAssets. This file is in the .gitignore.

***

### ARCore Extensions & Geospatial

For Geospatial to work, in the Unity editor go to Edit > Project Settings > XR Plug-in Management > ARCore Extensions. Then check Geospatial (and check iOS Support Enabled if building for iOS) and set up authorization. Follow the instructions in [this link](https://developers.google.com/ar/develop/unity-arf/geospatial/enable-android) for Android authentication or follow [this link](https://developers.google.com/ar/develop/unity-arf/geospatial/enable-ios) for iOS authentication. The easiest method for me was to use the previously mentioned API key for authentication, but be warned that your API key will be saved in `/ProjectSettings/ARCoreExtensionsProjectSettings.json` (this file is already in the .gitignore).

#### Troubleshooting ARCore Extensions

##### iOS

Having ARCore Extensions causes problems when trying to build the project in Xcode. Follow these steps:

* After building the Xcode project, open a terminal and run `pod install` in the project folder.

* Use the `.xcworkspace` file instead of `.xcodeproj` to open the project in Xcode. If you want to try using `.xcodeproj` (can't guarantee that it will work), in the Unity editors go to Assets > External Dependency Manager > iOS Resolver > Settings, then change the Cocoapods Integration type.

Here is a [useful reference](https://shobhitsamaria.com/cocoapods-installation-failure-while-building-unity-project-for-ios/).

##### Android

If the app shows a black screen instead of the camera, check the Assets/Plugins/Android directory. If there are not a bunch of generated `.aar` files there, then you have a problem with the Android dependency resolver. In the Unity editor, go to `Assets > External Dependency Manager > Android Resolver > Settings` and uncheck `Enable Resolution on Build`. When I unchecked this, the files in the Plugins/Android folder stopped being automatically deleted.