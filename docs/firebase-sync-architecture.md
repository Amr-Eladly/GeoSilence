# GeoSilence Firebase Auth and Cloud Sync Architecture

This plan keeps SQLite as the source of truth for app runtime behavior and adds Firebase as a durable, authenticated sync layer. Geofencing and mode switching continue to read from the local cache, so the app remains fast and usable offline.

## Goals

- Firebase Authentication with email/password and Google Sign-In.
- Apple Sign-In ready behind the same authentication interface.
- User profile with Firebase Auth UID, display name, and email.
- Firestore cloud storage for user-owned places.
- Offline-first sync that restores data after reinstall once the user logs in.
- Firestore rules that prevent users from modifying another user's places.

## Recommended Packages

Use the native Firebase client plugin path for Android/iOS:

```xml
<PackageReference Include="Plugin.Firebase" Version="4.2.1" />
<PackageReference Include="Plugin.Firebase.Auth" Version="5.0.1" />
<PackageReference Include="Plugin.Firebase.Firestore" Version="4.0.0" />
```

Current project packages to keep:

```xml
<PackageReference Include="sqlite-net-pcl" Version="1.9.172" />
<PackageReference Include="SQLitePCLRaw.bundle_green" Version="2.1.10" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
```

Alternative: implement Firebase Auth and Firestore through REST with `HttpClient`. Firebase Auth REST supports email/password sign-up and sign-in endpoints, and Firestore REST accepts a Firebase ID token in `Authorization: Bearer {token}`. The native plugin path is preferred because it aligns better with Google and Apple provider flows.

Sources checked on 2026-05-31:

- Firebase Auth REST: https://firebase.google.com/docs/reference/rest/auth
- Firestore REST authentication: https://firebase.google.com/docs/firestore/use-rest-api
- Firestore security rules: https://firebase.google.com/docs/firestore/security/get-started
- Firestore query/rule constraints: https://firebase.google.com/docs/firestore/security/rules-query
- NuGet Plugin.Firebase: https://www.nuget.org/packages/Plugin.Firebase/
- NuGet Plugin.Firebase.Auth: https://www.nuget.org/packages/Plugin.Firebase.Auth/

## Firestore Schema

Use Firebase Auth UID as the application user ID.

```text
users/{uid}
  uid: string
  displayName: string
  email: string
  createdAt: timestamp
  updatedAt: timestamp

users/{uid}/places/{placeId}
  id: string
  ownerId: string
  name: string
  latitude: number
  longitude: number
  radius: number
  mode: "Silent" | "Vibrate" | "Normal"
  isActive: boolean
  isPublic: boolean
  deleted: boolean
  createdAt: timestamp
  updatedAt: timestamp
  version: number

publicPlaces/{placeId}
  same safe public subset as place
```

Why a user subcollection: the core access pattern is "load my places", so `/users/{uid}/places` gives simple rules and efficient user-scoped sync. `publicPlaces` is optional for the future community map from `summary.md`; it avoids exposing user-private documents through broad collection group queries.

## Local SQLite Changes

Extend `PlaceEntity` instead of replacing it:

```csharp
public string CloudId { get; set; } = "";
public string OwnerId { get; set; } = "";
public bool IsPublic { get; set; }
public bool IsDeleted { get; set; }
public bool IsDirty { get; set; }
public DateTimeOffset CreatedAt { get; set; }
public DateTimeOffset UpdatedAt { get; set; }
public DateTimeOffset? LastSyncedAt { get; set; }
public long Version { get; set; }
```

Keep the current integer `Id` as the local primary key. Add `CloudId` as the stable Firestore document ID. New local places generate a GUID cloud ID before first upload, which makes offline-created places sync safely.

## Architecture

```text
Pages
  -> PageModels
    -> PlaceRepository
      -> SQLite local cache
      -> SyncService
        -> CloudPlaceRepository
          -> Firestore

PageModels
  -> AuthenticationService
    -> Firebase Auth
    -> SecureStorage token/session state
```

### AuthenticationService

Responsibilities:

- Sign up and sign in with email/password.
- Sign in with Google on Android/iOS.
- Prepare `SignInWithAppleAsync` in the interface and implement it when Apple entitlements and Firebase provider settings are ready.
- Expose `CurrentUser`, `IsSignedIn`, `AuthStateChanged`, and `GetIdTokenAsync`.
- Create or update `/users/{uid}` after successful login.
- Clear local user session on sign out.

Suggested contract:

```csharp
public interface IAuthenticationService
{
    GeoSilenceUser? CurrentUser { get; }
    bool IsSignedIn { get; }
    event EventHandler<AuthStateChangedEventArgs>? AuthStateChanged;

    Task<GeoSilenceUser> SignUpWithEmailAsync(string email, string password, string displayName);
    Task<GeoSilenceUser> SignInWithEmailAsync(string email, string password);
    Task<GeoSilenceUser> SignInWithGoogleAsync();
    Task<GeoSilenceUser> SignInWithAppleAsync();
    Task<string> GetIdTokenAsync(bool forceRefresh = false);
    Task SignOutAsync();
}
```

### CloudPlaceRepository

Responsibilities:

- Read all places under `/users/{uid}/places`.
- Upsert one place using `CloudId`.
- Soft-delete a place by setting `deleted = true`.
- Pull changes updated after the last sync checkpoint.
- Mirror public place documents to `/publicPlaces/{placeId}` only when `IsPublic = true`.

Suggested contract:

```csharp
public interface ICloudPlaceRepository
{
    Task<IReadOnlyList<CloudPlaceDto>> GetPlacesAsync(string userId, DateTimeOffset? changedAfter);
    Task UpsertPlaceAsync(string userId, CloudPlaceDto place);
    Task SoftDeletePlaceAsync(string userId, string cloudId, DateTimeOffset deletedAt);
}
```

### SyncService

Responsibilities:

- Run after login, app startup, local place changes, and network reconnect.
- Push local dirty rows first.
- Pull remote rows second.
- Merge into SQLite.
- Notify `HomeViewModel` to reload places and re-register geofences.
- Store per-user sync checkpoint in Preferences or a local `SyncStateEntity` table.

Conflict strategy:

- Use `UpdatedAt` and `Version`.
- Local dirty changes win only if their `UpdatedAt` is newer than Firestore.
- Remote deleted documents mark local rows deleted.
- UI queries filter out `IsDeleted`.

## Updated Repository Layer

`PlaceRepository` remains the only local place repository consumed by view models. It should no longer accept arbitrary caller-provided user IDs from UI code; it should ask `AuthenticationService` for the current UID.

Target behavior:

```csharp
public async Task<List<Place>> GetPlacesAsync()
{
    var userId = _auth.CurrentUser?.Uid ?? LocalUserIds.Anonymous;
    var entities = await _db.GetActivePlacesForUserAsync(userId);
    return entities.Select(MapToDomain).ToList();
}

public async Task AddPlaceAsync(Place place)
{
    var userId = RequireUserId();
    var entity = MapToEntity(place, userId);

    entity.CloudId = Guid.NewGuid().ToString("N");
    entity.IsDirty = true;
    entity.CreatedAt = _clock.UtcNow;
    entity.UpdatedAt = entity.CreatedAt;
    entity.Version = 1;

    await _db.InsertAsync(entity);
    place.Id = entity.Id;

    _ = _sync.RequestSyncAsync(SyncReason.LocalChange);
}

public async Task UpdatePlaceAsync(Place place)
{
    var entity = await _db.GetPlaceAsync(place.Id);
    EnsureOwner(entity);

    ApplyChanges(entity, place);
    entity.IsDirty = true;
    entity.UpdatedAt = _clock.UtcNow;
    entity.Version++;

    await _db.UpdateAsync(entity);
    _ = _sync.RequestSyncAsync(SyncReason.LocalChange);
}

public async Task DeletePlaceAsync(int id)
{
    var entity = await _db.GetPlaceAsync(id);
    EnsureOwner(entity);

    entity.IsDeleted = true;
    entity.IsDirty = true;
    entity.UpdatedAt = _clock.UtcNow;
    entity.Version++;

    await _db.UpdateAsync(entity);
    _ = _sync.RequestSyncAsync(SyncReason.LocalChange);
}
```

Important change: deletes become soft deletes locally and remotely so offline deletion syncs correctly.

## Automatic Sync Triggers

- After successful login: pull all remote places, merge into SQLite, then push unsynced local rows if migration is enabled.
- On app startup with existing session: sync in background.
- After add/update/delete: save SQLite immediately, mark dirty, queue sync.
- On connectivity change to internet: run sync.
- Periodic fallback: run sync every 15 minutes while app is foregrounded.

For anonymous pre-auth local data, show a one-time "keep these places in your account" migration after login. If accepted, assign the signed-in UID to local rows that still have `OwnerId = local_user`, mark them dirty, then sync.

## Reinstall Restore Flow

1. User installs app.
2. SQLite is empty.
3. User logs in.
4. `SyncService.RunInitialSyncAsync()` reads `/users/{uid}/places`.
5. Firestore rows are inserted into SQLite.
6. `HomeViewModel` reloads from SQLite.
7. Android background geofences are registered from restored local rows.

## Security Rules

The deployable rules live in `firebase/firestore.rules`.

They enforce:

- User profile document ID must equal `request.auth.uid`.
- Private places live under the signed-in user's document.
- A place write must set `ownerId` to the authenticated UID.
- Public places can be read by signed-in users only when `isPublic = true` and `deleted = false`.
- Query/list access is limited to user-owned subcollections.

## Firebase Console Setup

1. Create a Firebase project.
2. Add Android app with package ID from `GeoSilence.csproj`: `com.companyname.geosilence`.
3. Add iOS app with the final bundle identifier.
4. Enable Authentication providers:
   - Email/password.
   - Google.
   - Apple later, when iOS entitlements and Apple developer configuration are ready.
5. Create Firestore in production mode.
6. Deploy `firebase/firestore.rules`.
7. Add `google-services.json` to `Platforms/Android` and `GoogleService-Info.plist` to `Platforms/iOS`, using MAUI build actions recommended by the Firebase plugin.

## Implementation Plan

1. Add Firebase configuration files and package references.
2. Add `GeoSilenceUser`, auth event args, and auth interfaces.
3. Implement `AuthenticationService` for email/password and Google Sign-In.
4. Add Apple Sign-In interface method and a platform-not-supported placeholder for non-iOS.
5. Extend `PlaceEntity` with cloud sync metadata.
6. Add SQLite migration/backfill so existing rows receive `CloudId`, timestamps, `IsDirty = true`, and `OwnerId = local_user`.
7. Add `CloudPlaceDto` and mapping extensions.
8. Implement `CloudPlaceRepository`.
9. Implement `SyncService` with a single-flight lock so multiple triggers cannot run concurrent syncs.
10. Update `PlaceRepository` to soft-delete, mark dirty, and queue sync.
11. Replace hardcoded `USER_ID = "local_user"` in `HomeViewModel` with the authenticated user flow.
12. Add login/register pages and route unauthenticated users there before `MainPage`.
13. After login, run initial sync and reload places.
14. Register connectivity-change sync trigger.
15. Add Firestore emulator tests for security rules.
16. Add unit tests for merge conflict behavior.
17. Test reinstall restore on Android and iOS simulator/device.

## Production Notes

- Do not store Firebase refresh tokens in plain text; use platform secure storage or the native Firebase SDK session store.
- Add indexes only after Firestore asks for them. The private schema avoids most composite index needs.
- Keep geofencing local-only. Firestore sync should never be required to enter or exit a zone.
- Add Firebase App Check before public launch to reduce abusive API use.
- Add Crashlytics/Analytics later, but keep them out of the first sync implementation to reduce launch risk.
