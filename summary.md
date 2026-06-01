# GeoSilence

GeoSilence is a .NET MAUI mobile application that automatically changes the phone's sound profile based on the user's geographic location.

The application allows users to create places with configurable geofences and associated phone modes (Silent, Vibrate, Normal).

When the user enters or exits a configured area, GeoSilence automatically applies or restores the appropriate phone mode.

---

# Technology Stack

Frontend:
- .NET MAUI 9
- XAML
- MVVM
- CommunityToolkit.Mvvm

Database:
- SQLite
- sqlite-net-pcl

Maps:
- Microsoft.Maui.Controls.Maps
- Google Places API

Android:
- Android Geofencing API
- Broadcast Receivers
- Notification Policy Access (DND)

Architecture:
- MVVM
- Repository Pattern
- Service Layer

---

# Current Features

## Place Management

Users can:

- Add places
- Edit places
- Delete places
- Configure radius
- Configure desired mode
- View places on map

Supported modes:

- Silent
- Vibrate
- Normal

---

## Geofencing

Foreground geofencing:

- User location checked periodically
- Distance calculated against all places
- Mode applied automatically

Background geofencing:

- Android Geofencing API
- Geofence enter detection
- Geofence exit detection
- Works when app is closed

---

## Phone Mode Management

Android implementation supports:

- Silent
- Vibrate
- Normal

Requires:

- Notification Policy Access (DND)

ModeService responsibilities:

- Capture original mode
- Apply geofence mode
- Restore original mode after leaving all zones

---

## Search

Users can search places using:

Google Places Text Search API

Search result can:

- Display on map
- Be added directly as a saved place

---

## Persistence

Current storage:

SQLite database

Stored data:

- Places
- Radius
- Coordinates
- Mode
- Active status

Current storage scope:

Local device only

No cloud synchronization yet.

---

# Current Folder Responsibilities

## Models

Contains:

- Place
- Location
- ModeType
- PlaceEntity

Purpose:

Data structures used throughout the application.

---

## Services

Contains:

### LocationService

Responsibilities:

- Request location permissions
- Retrieve current GPS position

---

### GeofencingService

Responsibilities:

- Determine whether user is inside a place radius

---

### BackgroundGeofenceService

Responsibilities:

- Register Android geofences
- Manage Android Geofencing API

---

### ModeService

Responsibilities:

- Apply phone mode
- Restore original mode
- Manage DND interactions

---

### SearchService

Responsibilities:

- Query Google Places API

---

### DistanceService

Responsibilities:

- Distance calculations

---

### DatabaseService

Responsibilities:

- SQLite initialization

---

### PlaceRepository

Responsibilities:

- CRUD operations for places

---

## PageModels

### HomeViewModel

Responsibilities:

- Main application state
- Place loading
- Geofence handling
- UI updates

---

## Pages

### MainPage

Responsibilities:

- Map display
- Search UI
- Place management UI
- Bottom sheet UI

---

# Android Components

## MainActivity

Responsibilities:

- Application startup
- DND permission request

---

## GeofenceBroadcastReceiver

Responsibilities:

- Receive geofence enter events
- Receive geofence exit events
- Apply modes in background

---

## BootCompletedReceiver

Responsibilities:

- Re-register geofences after device reboot

---

# Current Permissions

Required Android permissions:

- INTERNET
- ACCESS_NETWORK_STATE
- ACCESS_FINE_LOCATION
- ACCESS_COARSE_LOCATION
- ACCESS_BACKGROUND_LOCATION
- ACCESS_NOTIFICATION_POLICY
- RECEIVE_BOOT_COMPLETED

---

# Current User Flow

1. User opens app

2. User adds a place

3. User selects:
   - Name
   - Radius
   - Mode

4. Place saved to SQLite

5. Geofence registered

6. User enters area

7. Mode automatically applied

8. User exits area

9. Original mode restored

---

# Known Completed Work

✔ Foreground geofencing

✔ Background geofencing

✔ Google Places search

✔ Map integration

✔ DND mode switching

✔ Place CRUD

✔ SQLite persistence

✔ Boot recovery

---

# Planned Features

## Authentication

Goal:

Allow users to create accounts.

Recommended:

Firebase Authentication

Providers:

- Email + Password
- Google Sign-In

---

## Cloud Sync

Goal:

Prevent loss of places after reinstalling app.

Recommended:

Firebase Firestore

Store:

- User profile
- Places
- Preferences

---

## Place Visibility

Add:

### Private Place

Visible only to owner.

### Public Place

Visible to all users but just as a place (pin and name on map) without embedded users values!

---

## Place Activation Type

Add:

### Primary

Automatic mode switching.

### Optional

Show notification:

"You entered <place>"

Options:

- Activate
- Ignore

---

## Community Map

Display public places from Firestore.

Features:

- Public pins
- Search public places
- Add public place to personal list

---

## UI Modernization

Potential additions:

- Material Design styling
- Animations
- Ripple effects
- Better colors
- Floating Action Buttons
- Bottom sheet improvements
- Dark mode
- Place category icons

---

# Future Platforms

Android:
- Full support

iOS:
- Investigation required

Limitation:

iOS does not allow apps to directly change system sound modes.

Possible approach:

Location-based notifications suggesting mode changes.

---

# Architecture Rules

1. Use MVVM.

2. Keep business logic inside Services.

3. Keep UI logic inside Pages.

4. Database access only through Repository layer.

5. Do not access SQLite directly from UI.

6. New features should be implemented through dependency injection.

7. Preserve separation between foreground and background geofencing systems.