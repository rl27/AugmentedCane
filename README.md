# AugmentedCane
Unity implementation of the Augmented Cane.

To use GPS navigation and TTS, you will need a Google Cloud API key. You will need to enable the Cloud Text-to-Speech API and the Routes API. Place your API key in a file named `apikey.txt` in Assets/StreamingAssets.

***

## ARCore Extensions & Geospatial

For Geospatial to work, go to Edit > Project Settings > XR Plug-in Management > ARCore Extensions. Then check Geospatial (and check iOS Support Enabled if building for iOS) and set up authorization. Follow the instructions in [this link](https://developers.google.com/ar/develop/unity-arf/geospatial/enable-android) for Android authentication or follow [this link](https://developers.google.com/ar/develop/unity-arf/geospatial/enable-ios) for iOS authentication. The easiest method for me was to use the previously mentioned API key for authentication, but be warned that your API key will be saved in `/ProjectSettings/ARCoreExtensionsProjectSettings.json` (this file is already in the .gitignore).

Having ARCore Extensions causes problems when trying to build the project in Xcode. Take the following steps.

For iOS, after building the Xcode project, open a terminal and run `pod install` in the project folder.

Then use the `.xcworkspace` file instead of `.xcodeproj` to open the project in Xcode. If you want to try using `.xcodeproj` (can't guarantee that it will work), in Unity go to Assets > External Dependency Manager > iOS Resolver > Settings, then change the Cocoapods Integration type.

Here is a [useful reference](https://shobhitsamaria.com/cocoapods-installation-failure-while-building-unity-project-for-ios/).