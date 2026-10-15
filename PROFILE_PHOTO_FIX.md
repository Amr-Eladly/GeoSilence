# Previous photo shown after camera capture — 15 October 2026

The previous implementation copied every camera/gallery result over the same `{userId}.jpg` file. Account's avatar used a file image source and compared only that path before refreshing. Replacing file contents did not give the renderer a new image identity. This is a concrete stale-display path; it does not prove old bytes were uploaded.

This checkout has an empty Firebase Storage bucket, so FirebaseStorageService.IsConfigured is false: photo bytes are stored locally, and the existing profile metadata is saved to Firestore. No storage bucket was configured by this fix.

## Changes

- Copy the camera/picker result immediately into a unique capture snapshot, before profile loading can access the avatar cache.
- If cloud photo storage is configured, upload that exact snapshot and use a cache path derived from the new remote object identity. Reopening the same remote photo reuses the same cache path; a replacement has a different path.
- Avatar rendering uses a fresh stream source, preventing native filename-cache reuse for local-only replacements too.
- Keep the previous remote object until the new profile metadata is saved. Restore the previous profile fields on metadata-save failure. Temporary capture snapshots are removed after the operation.

## Validation

`obj/profile-photo-check/` links the actual ProfilePhotoFiles helper and FirebaseStorageService into a console regression check. A fake HTTP handler captures the real upload-request body without contacting Firebase. It verifies:

1. A new capture is independent of the old gallery/avatar file.
2. Overwriting the legacy cache cannot change captured bytes.
3. The upload request contains the new capture bytes.
4. Replacing a remote photo changes the cache filename.
5. Reopening the same remote photo reuses its cache filename.
6. The published local avatar contains exactly the uploaded bytes.

All checks passed. The full Android build passed with 0 errors and 83 warnings; output is in `obj/profile-photo-fix-build.log`. The emulator received hash-verified updated managed assemblies and completed startup with no crash-buffer entries in the inspected process. End-to-end camera capture plus cloud upload has not been verified against a real configured storage bucket; the regression handler deliberately uses a test-only bucket and intercepts all requests.

The previously requested Places spinner/view reuse and Account count removal remain implemented; their validation is in PLACES_TAB_INVESTIGATION.md.
