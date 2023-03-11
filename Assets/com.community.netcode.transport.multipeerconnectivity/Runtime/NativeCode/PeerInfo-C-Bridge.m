#import <Foundation/Foundation.h>
#include "PeerInfo.h"

typedef void* ManagedPeerInfo;

ManagedPeerInfo UnityMC_Delegate_initWithPeerID(void* peerID, void* displayName, char startMode)
{
    PeerInfo* delegate = [[PeerInfo alloc] initWithPeerID:(__bridge NSUUID *)peerID
                                                displayName:(__bridge NSString *)displayName
                                                startMode:startMode];
    return (__bridge_retained void*)delegate;
}

void* UnityMC_PeerInfo_peerID(void* ptr)
{
    NSUUID* peerID = ((__bridge PeerInfo*)ptr).peerID;
    return (__bridge_retained void*)peerID;
}

void* UnityMC_PeerInfo_displayName(void* ptr)
{
    NSString* displayName = ((__bridge PeerInfo*)ptr).displayName;
    return (__bridge_retained void*)displayName;
}

char UnityMC_PeerInfo_startMode(void* ptr)
{
    return ((__bridge PeerInfo*)ptr).startMode;
}

char UnityMC_PeerInfo_mode(void* ptr)
{
    return ((__bridge PeerInfo*)ptr).mode;
}

void UnityMC_PeerInfo_setMode(void* ptr char mode)
{
    ((__bridge PeerInfo*)ptr).mode = mode;
}

char UnityMC_PeerInfo_peerCount(void* ptr)
{
    return ((__bridge PeerInfo*)ptr).peerCount;
}

void UnityMC_PeerInfo_setPeerCount(void* ptr char peerCount)
{
    ((__bridge PeerInfo*)ptr).peerCount = peerCount;
}

void* UnityMC_PeerInfo_serverPeerID(void* ptr)
{
    NSUUID* serverPeerID = ((__bridge PeerInfo*)ptr).serverPeerID;
    return (__bridge_retained void*)serverPeerID;
}

void UnityMC_PeerInfo_setServerPeerID(void* ptr void* serverPeerID)
{
    NSUUID* serverUUID = (__bridge NSUUID*)serverPeerID);
    ((__bridge PeerInfo*)ptr).serverPeerID = serverUUID;
}
