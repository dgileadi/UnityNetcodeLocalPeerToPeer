#import "PeerMessage.h"

@implementation PeerMessage

- (nullable instancetype)initWithPeerID:(nonnull NSString *)peerID data:(nonnull NSData *)data
{
    if (self = [super init])
    {
        self->_peerID = peerID;
        self->_data = data;
    }

    return self;
}

@end
