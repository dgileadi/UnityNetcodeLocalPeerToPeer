#import <Foundation/Foundation.h>
#include "PeerInfo.h"

typedef void* ManagedPeerInfo;

ManagedPeerInfo UnityMC_Delegate_initWithPeerID(void* peerID, void* displayName, char mode)
{
    PeerInfo* delegate = [[PeerInfo alloc] initWithPeerID:(__bridge NSString *)peerID
                                                displayName:(__bridge NSString *)displayName
                                                mode:mode];
    return (__bridge_retained void*)delegate;
}

void* UnityMC_PeerInfo_peerID(void* ptr)
{
    NSString* peerID = ((__bridge PeerInfo*)ptr).peerID;
    return (__bridge_retained void*)peerID;
}

void* UnityMC_PeerInfo_displayName(void* ptr)
{
    NSString* displayName = ((__bridge PeerInfo*)ptr).displayName;
    return (__bridge_retained void*)displayName;
}

char UnityMC_PeerInfo_mode(void* ptr)
{
    return ((__bridge PeerInfo*)ptr).mode;
}
