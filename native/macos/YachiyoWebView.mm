#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>
#import <QuartzCore/QuartzCore.h>
#include <mutex>
#include <string>

@interface YachiyoWebView : WKWebView
@property NSRect avatarHole;
@end
@implementation YachiyoWebView
- (NSView *)hitTest:(NSPoint)point {
 NSPoint local=[self convertPoint:point fromView:self.superview];
 if(NSPointInRect(local,self.avatarHole))return nil;
 return [super hitTest:point];
}
@end
static YachiyoWebView *directorView;
static NSString *localOrigin;
static bool verificationEnabled=false;
static std::mutex resultMutex;
static std::string verificationResult="{}",layoutResult="{}";
static id inputMonitor;
static bool cameraDragging=false,cameraPan=false;
static NSPoint cameraPrevious;
static float cameraDx=0,cameraDy=0,cameraZoom=0;
@interface YachiyoNavigation : NSObject <WKNavigationDelegate,WKScriptMessageHandler>
@end
@implementation YachiyoNavigation
- (void)userContentController:(WKUserContentController *)controller didReceiveScriptMessage:(WKScriptMessage *)message {
 if (![message.body isKindOfClass:[NSDictionary class]]) return;
 if (![NSJSONSerialization isValidJSONObject:message.body]) return;
 if([message.name isEqualToString:@"yachiyoLayout"]){
  NSDictionary *d=message.body;CGFloat x=[d[@"x"] doubleValue],y=[d[@"y"] doubleValue],w=[d[@"width"] doubleValue],h=[d[@"height"] doubleValue];
  bool visible=[d[@"visible"] boolValue]&&w>1&&h>1;
  CGFloat viewY=directorView.isFlipped?y:directorView.bounds.size.height-y-h;
  directorView.avatarHole=visible?NSMakeRect(x,viewY,w,h):NSZeroRect;
  CAShapeLayer *mask=[CAShapeLayer layer];mask.frame=directorView.bounds;mask.fillRule=kCAFillRuleEvenOdd;
  CGMutablePathRef path=CGPathCreateMutable();CGPathAddRect(path,NULL,directorView.bounds);
  if(visible){CGFloat layerY=directorView.layer.geometryFlipped?y:directorView.bounds.size.height-y-h;CGPathAddRect(path,NULL,CGRectMake(x,layerY,w,h));}
  mask.path=path;CGPathRelease(path);directorView.layer.mask=mask;
  NSData *json=[NSJSONSerialization dataWithJSONObject:d options:0 error:nil];
  std::lock_guard<std::mutex> guard(resultMutex);layoutResult.assign((const char *)json.bytes,json.length);return;
 }
 if(!verificationEnabled)return;
 NSData *data=[NSJSONSerialization dataWithJSONObject:message.body options:0 error:nil];
 if (data.length>65536) return;
 std::lock_guard<std::mutex> guard(resultMutex);verificationResult.assign((const char *)data.bytes,data.length);
}
- (void)webView:(WKWebView *)view decidePolicyForNavigationAction:(WKNavigationAction *)action decisionHandler:(void (^)(WKNavigationActionPolicy))reply {
 NSURL *url = action.request.URL;
 NSString *origin = [NSString stringWithFormat:@"%@://%@:%@", url.scheme, url.host, url.port];
 if ([origin isEqualToString:localOrigin]) { reply(WKNavigationActionPolicyAllow); return; }
 if (action.navigationType == WKNavigationTypeLinkActivated && [url.scheme isEqualToString:@"https"]) [[NSWorkspace sharedWorkspace] openURL:url];
 reply(WKNavigationActionPolicyCancel);
}
@end
static YachiyoNavigation *navigation;
static void layout() {
 NSView *parent = directorView.superview;
 if (!parent) return;
 NSRect bounds = parent.bounds;
 directorView.frame = bounds;
}
extern "C" void YachiyoWeb_Open(const char *address) {
 NSString *urlString = [NSString stringWithUTF8String:address];
 dispatch_async(dispatch_get_main_queue(), ^{
  if (directorView) return;
  NSWindow *window = NSApp.mainWindow ?: NSApp.keyWindow;
  if (!window) for (NSWindow *candidate in NSApp.windows) if (candidate.isVisible && (candidate.styleMask & NSWindowStyleMaskTitled)) { window=candidate; break; }
  if (!window) return;
  NSURL *url=[NSURL URLWithString:urlString];
  localOrigin=[NSString stringWithFormat:@"%@://%@:%@",url.scheme,url.host,url.port];
  WKWebViewConfiguration *config=[WKWebViewConfiguration new];
  if(verificationEnabled)config.websiteDataStore=[WKWebsiteDataStore nonPersistentDataStore];
  config.mediaTypesRequiringUserActionForPlayback=WKAudiovisualMediaTypeNone;
  WKUserScript *runtime=[[WKUserScript alloc] initWithSource:@"window.yachiyoDesktop=Object.freeze({embeddedMemory:true,platform:'darwin',unified:true});" injectionTime:WKUserScriptInjectionTimeAtDocumentStart forMainFrameOnly:YES];
  [config.userContentController addUserScript:runtime];
  directorView=[[YachiyoWebView alloc] initWithFrame:NSZeroRect configuration:config];
  navigation=[YachiyoNavigation new];directorView.navigationDelegate=navigation;directorView.wantsLayer=YES;
  [config.userContentController addScriptMessageHandler:navigation name:@"yachiyoLayout"];
  if(verificationEnabled)[config.userContentController addScriptMessageHandler:navigation name:@"yachiyoVerification"];
  directorView.autoresizingMask=NSViewWidthSizable|NSViewHeightSizable;
  [window.contentView addSubview:directorView positioned:NSWindowAbove relativeTo:nil];layout();
  inputMonitor=[NSEvent addLocalMonitorForEventsMatchingMask:(NSEventMaskLeftMouseDown|NSEventMaskLeftMouseUp|NSEventMaskLeftMouseDragged|NSEventMaskOtherMouseDown|NSEventMaskOtherMouseUp|NSEventMaskOtherMouseDragged|NSEventMaskScrollWheel) handler:^NSEvent *(NSEvent *event){
   if(event.window!=directorView.window)return event;
   NSPoint point=[directorView convertPoint:event.locationInWindow fromView:nil];bool inside=NSPointInRect(point,directorView.avatarHole);
   std::lock_guard<std::mutex> guard(resultMutex);
   if(event.type==NSEventTypeLeftMouseDown||event.type==NSEventTypeOtherMouseDown){cameraDragging=inside;cameraPrevious=point;cameraPan=event.type==NSEventTypeOtherMouseDown||(event.modifierFlags&NSEventModifierFlagShift);}
   if(cameraDragging&&(event.type==NSEventTypeLeftMouseDragged||event.type==NSEventTypeOtherMouseDragged||event.type==NSEventTypeLeftMouseUp||event.type==NSEventTypeOtherMouseUp)){cameraDx+=point.x-cameraPrevious.x;cameraDy+=(directorView.isFlipped?1:-1)*(point.y-cameraPrevious.y);cameraPrevious=point;}
   if((event.type==NSEventTypeLeftMouseDragged||event.type==NSEventTypeOtherMouseDragged)&&cameraDragging)return nil;
   if(event.type==NSEventTypeLeftMouseUp||event.type==NSEventTypeOtherMouseUp){bool consume=cameraDragging;cameraDragging=false;if(consume)return nil;}
   if(event.type==NSEventTypeScrollWheel&&inside){cameraZoom+=event.scrollingDeltaY;return nil;}
   return inside?nil:event;
  }];
  [directorView loadRequest:[NSURLRequest requestWithURL:url]];
 });
}
extern "C" void YachiyoWeb_Layout(float widthFraction,float headerFraction) {
 dispatch_async(dispatch_get_main_queue(), ^{layout();});
}
extern "C" void YachiyoWeb_Close() {
 dispatch_async(dispatch_get_main_queue(), ^{if(inputMonitor)[NSEvent removeMonitor:inputMonitor];inputMonitor=nil;[directorView stopLoading];[directorView removeFromSuperview];directorView=nil;navigation=nil;});
}
extern "C" void YachiyoWeb_Evaluate(const char *script) {
 NSString *code=[NSString stringWithUTF8String:script];
 dispatch_async(dispatch_get_main_queue(), ^{[directorView evaluateJavaScript:code completionHandler:nil];});
}
extern "C" void YachiyoWeb_EnableVerification() {verificationEnabled=true;}
extern "C" const char *YachiyoWeb_Verification() {
 static thread_local std::string copy;std::lock_guard<std::mutex> guard(resultMutex);copy=verificationResult;return copy.c_str();
}

extern "C" const char *YachiyoWeb_Viewport() {
 static thread_local std::string copy;std::lock_guard<std::mutex> guard(resultMutex);copy=layoutResult;return copy.c_str();
}
extern "C" void YachiyoWeb_CameraInput(float *values) {
 std::lock_guard<std::mutex> guard(resultMutex);values[0]=cameraDx;values[1]=cameraDy;values[2]=cameraZoom;values[3]=cameraPan?1:0;cameraDx=cameraDy=cameraZoom=0;
}
