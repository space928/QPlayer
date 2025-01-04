# QPlayer Video Plugin

### Architecture

```
VideoCueViewModel
	OnLoad
	\--> acquires a VideoDecoder from the pool, which fills the frameQueue
		 creates a VideObject

	OnPlay
	\--> registers 

VideoFrame
	private OGLTexture texture (from a pool)
	private Memory<float> audio


VideoDecoder (pooled)
	private frameQueueTarget? --> ref to the queue to write too
	private AVContext* context --> libav context for decoding
	event OnError --> invoked when an unhandled exception occurs, the owner of the decoder should handle this by acquiring a new decoder from the pool.


VideoWindow
	owns the OpenGL context
	owns the video window thread which clocks the playback of video frames
	OnFrame
	\--> get the next frame from the VideoCompositor

VideoCompositor
	maintains a list of VideoObjects to be composited into the final buffer, this final buffer then gets used in the output shader which applies final colour correction/geometry.

VideoObject
	prop Mesh
	prop Mat
	private vm
	private playbackTime
	private ConcurrentQueue<VideoFrame> frameQueue;
	private VideoDecoder? decoder
	private bool isPlaying

	ctor
	\--> acquire decoder and initialise it, on decoder error auto restart up to N times  

	OnPlay
	\--> playbackTime = vm.StartTime
		 reset auto restart counter
		 isPlaying = true

	OnFrame (globalTime, globalFrame, gl)
	\--> return false if not currently playing and not paused
		 Update any time specific shader vars

```

