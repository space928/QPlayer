---
title: OSC Reference
---

QPlayer can be controlled from external applications over the network using 
[OSC messages](http://opensoundcontrol.org/). At the moment only UDP OSC messages are 
supported. Configuration settings for sending and receiving OSC messages can be found
in the [Project Setup](project-setup).

The following OSC messages can be received by QPlayer:

## Go
```
qplayer/go,[qid],[select]
```

## Pause
```
qplayer/pause,[qid]
```

## Unpause
```
qplayer/unpause,[qid]
```

## Stop
```
qplayer/stop,[qid]
```

## Preload
```
qplayer/preload,[qid],[time]
```

## Select Cue
```
qplayer/select,[qid]
```

## Select Previous Cue
```
qplayer/up
```

## Select Next Cue
```
qplayer/down
```

## Save Project File
```
qplayer/save
```

<!--![Cue Stack](../../../assets/hints.png)-->