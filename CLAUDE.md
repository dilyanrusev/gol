I want to implement game-of-life, as per @TASK.md.

I want to use the following:

* ASP.NET Core for the server
* SignalR for communication
* TS->JS + Canvas for the client(s) for observing a universe under a page (see below)
* Simple web form with ASP.NET Core Pages for
  * uploading a seed pattern (.RLE files)
  * creating custom seed
  * buttons for saving the current state of the server to .RLE
  * buttons for changing the viewport (origin for the client + grid size)
  * JS (or a library) for touch gestures to move the viewport
* Bootstrap for the UI
