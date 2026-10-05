# DirectY (TungMEP2 PLUMBING) design

## Goal

Connect a selected branch Pipe's open end directly to the open branch port of an existing three-port Y fitting. The user selects the two objects, as with 1E1Y. No near-Y elbow is required or deleted for this primary path. The upstream elbows/branch remain in place.

## Supported first topology

The source open-end Pipe is connected at its other end to a two-port elbow, which is connected to a straight upstream Pipe. The upstream Pipe is the rotation and sliding axis. The elbow and source Pipe form the only movable pair. The Y remains fixed and its other connections are untouched. Reject ambiguous branches, multiple external connections, pinned/grouped elements, incompatible diameters, or geometry that cannot close with one rotation about the upstream axis, one axial slide, and resizing only the two local Pipes.

## Geometry and safety

Rotate the elbow/source pair about the upstream Pipe axis so the source open-end connector faces opposite the Y's open connector. Slide that pair along the axis, adjust the upstream Pipe's near endpoint and the source Pipe's open endpoint, then directly ConnectTo the Y. Preserve far endpoints, IDs, local elbow connection, Y's existing connections, and untouched network. All changes occur in one Revit transaction; any failed validation rolls it back. A zero/near-zero length Pipe is invalid. Report unsupported geometry instead of creating a kink or moving the wider network.

## Acceptance

- `DirectY` appears in TungMEP2 → PLUMBING beside 1E1Y.
- Picks an existing Y and an open-end branch Pipe; the open-end workflow never deletes any fitting.
- After success, the Pipe is directly connected to the Y, connector origins coincide and axes oppose; no extra elbow or Tee is created.
- Unsafe/unsupported cases leave the model unchanged and show a useful reason.
- Revit 2024 build and packaged installer succeed. The exact model geometry still requires an in-Revit smoke test.

## Deferred topology

A branch already ending in an elbow adjacent to the Y is not automatically deleted in this first version. It needs a separately validated topology-removal path; do not silently delete a fitting.

## Flexible upgrade (2026-10-01)

The user's follow-up changes the first pick to a Pipe on the existing Y main run, then the terminal open branch Pipe. Search a bounded collinear run for an eligible angled Y with the same open-port DN; do not rank partial searches or let unsupported 90-degree tees shadow angled Y candidates. Reject equally near eligible Ys.

Trace selected terminal Pipe through straight Pipes and two-port inline reducers/unions to its elbow. Keep that tail rigid. Prefer the original one-joint solve when possible. If needed and available, use the anchor Pipe's far elbow plus its directly connected upstream straight Pipe as a second swivel: outer rigid group rotates/slides around the upstream Pipe, inner tail rotates/slides around the transformed anchor Pipe. Resize only upstream/anchor/terminal source Pipes, preserving the fixed upstream far endpoint and all external bindings.

Require local element identity/type, connector DN, direct source–Y adjacency, each prior binding, fixed external connector frames, and rigid fitting/intermediate-Pipe connector distance and relative-angle signatures. Roll back any failed postcondition or Revit failure. Existing-Y-only mode, no fitting deletion, no new Y or perpendicular-tee routing.
