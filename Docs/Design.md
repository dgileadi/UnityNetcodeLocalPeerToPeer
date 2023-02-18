# Peer to Peer Netcode Scenarios

## The game starts network connectivity

The network layer starts advertising and looking for peers before
`NetworkManager.StartServer` or `NetworkManager.StartClient` has been called.

- Triggers:
  - call to `NetworkManager.StartPeerToPeer`
- Do:
  - maybe shutdown NetworkManager if it was running
  - change the mode from `stopped` to `pairing`, which:
  - starts advertising and discovering

## Two peers meet who don't have any peers (and thus no server)

I run some algorithm to pick a server, one becomes a server, and tells its NetworkManager to start as a server. The client stops advertising/browsing for peers but the server continues.

- Triggers:
  - transport gets a "peer discovered" event, with some advertisement data that indicates that neither client is a server
- Do:
  - transport calls back with a decision on whether to connect
  - if so, transport initiates a connection
  - both peers accept the connection
  - if a two-step server picking process, peers send additional server-picking message
  - server calls `NetworkManager.StartServer` and client calls `NetworkManager.StartClient`
  - server stops discovering?
  - client stops advertising and discovering

## A serverless peer and an existing server meet

The peer joins as a client and stops advertising/browsing for peers.

- Triggers:
  - transport gets a "peer discovered" event, with some advertisement data that indicates that the peer is a server
- Do:
  - transport calls back with a decision on whether to connect
  - if so, transport initiates a connection
  - server transport calls back with a decision on whether to accept the connection
  - if so, client transport calls `NetworkManager.StartClient`
  - client stops advertising and discovering
  - if server transport hit the client limit, it stops advertising and sends a message to all clients to become relays

## A connected client gets a message to become a relay

- Triggers:
  - a client gets a "hey, become a relay" message
- Do:
  - if already a relay, do nothing
  - otherwise,
    - switch to relay mode
    - start advertising

## A relay gets a connection from a new peer

- Triggers:
  - a "peer discovered" event from a peer that isn't a server
- Do:
  - peer transport calls back with a decision on whether to connect
  - if so, transport initiates a connection
  - relay transport calls back with a decision on whether to accept the connection
  - if so, client transport calls `NetworkManager.StartClient`
  - client stops advertising and discovering
  - if relay transport hit the client limit, it stops advertising and sends a message to all clients to become relays
  - relay sends message to parent of the user ID of the new client
  - parent stores the fact that it should send messages to that client to the relay instead

## A peer meets a server that already is full of clients

This may not be possible because perhaps the server should stop advertising/browsing for peers once it's full. The downside is that players may not be able to connect to a session that they'd want to.

I think my options are:

- Create more MCSession objects
+ Start a relay system
- Don't allow connection (ignore)

- Trigger:
  - server (or relay) gets a connection request when full
- Do:
  - refuse

## Two peers meet that are both servers

TODO:

Some options:

- Gotta keep 'em separated
- Pick a server and join them, abandoning (merging?) the other session (but how do you become a client when you're a server?)
  - Maybe with the 2nd server as a relay
- Kill the session and let the network rebuild itself

I think the code needs to have a callback to help determine whether to merge sessions—or maybe it uses the same discriminator as the one for whether peers should connect to each other. Maybe if they were already part of the same session in the past they should auto-reconnect.

Or maybe I should handle this at the disconnection point—there should be some way to decide, on disconnection from a server, whether to quietly go into that good night or whether to escalate it to the player.

Note that **there's no way for this to happen unless servers are still trying to discover other servers**. So perhaps a server needs to continue browsing for peers, even after it stops advertising... Although if the reconnect option is always going to be "abort" then maybe this behavior needs to be turn-on-able.

- Triggers:
  - a server gets an advertisement from a peer for the same session ID that's also a server
- Do:
  - call back to see what to do:
    - stay apart
      - do nothing, abort
    - join
      - run server-picking code (this may perhaps be more complex than normal server-picking code, using relay depth, etc.)
      - the winner does nothing
      - the loser calls `NetworkManager.Shutdown` but does not destroy the network of clients/peers at the transport level
      - the loser tries connecting to the other server (or its relay)

## A server loses connection to a client

- Triggers:
  - an "I got disconnected" event
  - a heartbeat fails to be received after X seconds (do clients send heartbeats? can we tell whether a server message failed to be delivered? maybe heartbeats use "reliable" messaging)
- Do:
  - if previously full of clients, start advertising again
  - if lost its last client,
    - start browsing for peers
    - (after a delay?) call `NetworkManager.Shutdown`
      - what happens to the game objects? transfer control...?
    - the mode becomes `pairing` again

## A client loses connection to a server (or maybe misses a heartbeat or two)

There needs to be a decision point here for what to do. For some sessions a quiet disconnect and attempt to reconnect (or connect to someone else) is perfect. For other sessions we need to escalate the event to the player and have them move into range, or abort the session, or something similar.

In any case the client starts advertising/browsing for peers. It probably also needs to shutdown its NetworkManager (maybe after a delay), certainly if it starts connecting to a different server.

In the mean time it needs to immediately take over all its own animation, physics, AI, etc.

The difference in behavior comes down to whether it should try reconnecting to the same session or should go ahead and look for other sessions, etc.

- Triggers:
  - an "I got disconnected" event
  - a heartbeat fails to be received after X seconds
- Do:
  - immediately take over animation, physics, AI, etc.?
  - call a callback to determine what to do
    - abort the session and start discovery again
      - call `NetworkManager.Shutdown`
      - start advertising and browsing for peers
    - wait longer to reconnect
      - if the trigger was a "disconnected" event, start discovering peers
    - pick a new server
      - TODO: if the client is a relay then it might immediately become a server?
      - TODO: perhaps clients should keep track of other peers and try connecting to one of them?

## A client reconnects to a server it was connected to before

- Triggers:
  - a "peer discovered" event, with the peer being the server
- Do:
  - connect to the peer again

Hopefully the higher networking layers gracefully handle the reconnection (needs research)

## A client reconnects to a relay of a server it was directly connected to before

Probably just behave the same as if it had reconnected directly to the server

## Notes

- Transports should start advertising/browsing before the server has started, and should continue after it shuts down. This implies a separate startup/shutdown process for them.
- `UnityTransport` supports heartbeat messages; I should probably copy that (and maybe other features).

## How relay might work

I think this needs to be at the transport level. What it means is that a relay peer has to pretend to be a server at the transport level, but not at the Netcode for GameObjects level. That way the server talks to all its clients, but one or more of the clients knows it has a list of relay peers that it forwards messages back and forth with.

If a relay peer disconnects from the server then:

- Maybe we can see if someone else can reach the server, and make it the relay peer
- Maybe we can make the relay peer a server (but how?)
- Maybe we can treat it as all the clients disconnecting, and let the network rebuild itself

How does a peer become a relay?

If a server starts to get full, maybe it tells some/all of its peers to become relays? Then they'd start advertising (but not browsing?) and let other clients connect to them. We want the network to be as flat as possible so as to avoid lots of hops.

## How peers know whether to connect

In my case everyone is in a shared world, but that may not be the case for every game. Maybe I need to make a discriminator (like a game session name) that's sent along with the advertising info, where clients can choose whether to connect. Or maybe I need to make a C# handler for deciding whether to connect.

## [Nearby Connections API](https://developers.google.com/nearby/connections/overview) on Android

See <https://stackoverflow.com/questions/52773197/be-able-to-send-messages-bytes-simultaneous-to-multiple-devices-using-nearby-con>
 for how to connect to many devices, basically by making a snake topology.

### Picking a server

If I use the more powerful `P2P_STAR` which sometimes uses wifi (7 peers?) rather than bluetooth with its 2-3 peer limit, I can't easily negotiate to choose a server.

Maybe I can have a peer start discovery first and become a server if no other server is found (which cause a delay). Then when two servers meet each other, perhaps the one with fewest clients can stop being a server and give up. If none have any clients then we could use some other criteria for giving up.

Note that I've been discussing a fancy algorithm for fairly choosing a server, which would work on iOS, but really I don't know if the stakes are that high on choosing a server...

Speaking of that algorithm, though, I think I could make it one-shot if I frequently change the guess, and then advertise the guess to start with. Note that no matter how fair I try to make this, **a motivated peer can always circumvent it by claiming to already have more clients or by refusing to connect and then trying again and again.** In other words, it might not be worth it.

So on Android, perhaps peers start advertising and discovery for `P2P_STAR` mode, and then use a one-shot algorithm for choosing one of them to be a server. Once a server-to-be gains a client it starts the NetworkManager as a server and turns off discovery. Once it loses its last client it shuts down the NetworkManager as a server and turns on discovery again.

I don't know how I'd do relay with Nearby Connections and `P2P_STAR`.

Note that `P2P_CLUSTER` [may use wifi in the future](https://stackoverflow.com/questions/48730753/how-can-i-force-google-nearby-to-use-wifi-direct/48750716#comment116500754_48750716).

## What peers should advertise

- their UUID
- their display name
- their session ID or type
- the number of connected clients (if a server)
- the ID of the server (if a relay) (?)
- other stuff? user-specified stuff?
