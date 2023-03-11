#import <Foundation/Foundation.h>
#include "PeerMessage.h"

void* UnityMC_PeerMessage_peerID(void* ptr)
{
    NSUUID* peerID = ((__bridge PeerMessage*)ptr).peerID;
    return (__bridge_retained void*)peerID;
}

void* UnityMC_PeerMessage_data(void* ptr)
{
    NSData* data = ((__bridge PeerMessage*)ptr).data;
    return (__bridge_retained void*)data;
}
