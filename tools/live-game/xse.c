// Minimal XTEST driver for an XWayland game window.
//   xse find <name-substring>            -> "<id> <x> <y> <w> <h> <name>" per match
//   xse activate <id>                    -> _NET_ACTIVE_WINDOW request + raise
//   xse key <chord> [hold-ms]            -> e.g. ctrl+shift+t, Return, F4, w
//   xse type <text>                      -> types ASCII text
//   xse click <x> <y> [button]           -> absolute root coordinates
//   xse move <x> <y>
//   xse rel <dx> <dy> [steps]            -> relative pointer motion (mouse look)
//   xse down <keysym> / up <keysym>      -> hold a key across calls
//   xse shot <id> <out.ppm>              -> XGetImage of the window
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/Xatom.h>
#include <X11/extensions/XTest.h>
#include <X11/keysym.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <unistd.h>
#include <ctype.h>

static Display *d;

static char *window_name(Window w)
{
    Atom net = XInternAtom(d, "_NET_WM_NAME", False), utf8 = XInternAtom(d, "UTF8_STRING", False);
    Atom type; int format; unsigned long n, after; unsigned char *data = NULL;
    if (XGetWindowProperty(d, w, net, 0, 1024, False, utf8, &type, &format, &n, &after, &data) == Success && data)
        return (char *)data;
    char *name = NULL;
    if (XFetchName(d, w, &name) && name) return name;
    return NULL;
}

static void walk(Window w, const char *needle)
{
    char *name = window_name(w);
    if (name && strcasestr(name, needle)) {
        XWindowAttributes a; XGetWindowAttributes(d, w, &a);
        int x, y; Window child;
        XTranslateCoordinates(d, w, DefaultRootWindow(d), 0, 0, &x, &y, &child);
        if (a.map_state == IsViewable) printf("0x%lx %d %d %d %d %s\n", w, x, y, a.width, a.height, name);
    }
    if (name) XFree(name);
    Window root, parent, *kids; unsigned int n;
    if (XQueryTree(d, w, &root, &parent, &kids, &n)) {
        for (unsigned int i = 0; i < n; i++) walk(kids[i], needle);
        if (kids) XFree(kids);
    }
}

static KeySym sym(const char *s)
{
    if (!strcasecmp(s, "ctrl")) return XK_Control_L;
    if (!strcasecmp(s, "shift")) return XK_Shift_L;
    if (!strcasecmp(s, "alt")) return XK_Alt_L;
    if (!strcasecmp(s, "enter") || !strcasecmp(s, "return")) return XK_Return;
    if (!strcasecmp(s, "esc") || !strcasecmp(s, "escape")) return XK_Escape;
    if (!strcasecmp(s, "space")) return XK_space;
    if (!strcasecmp(s, "slash")) return XK_slash;
    if (!strcasecmp(s, "equal")) return XK_equal;
    if (!strcasecmp(s, "tab")) return XK_Tab;
    KeySym k = XStringToKeysym(s);
    if (k == NoSymbol && strlen(s) == 1) k = (KeySym)tolower((unsigned char)s[0]);
    return k;
}

static void press(KeySym k, Bool down)
{
    KeyCode c = XKeysymToKeycode(d, k);
    if (!c) { fprintf(stderr, "no keycode for keysym %lu\n", k); return; }
    XTestFakeKeyEvent(d, c, down, CurrentTime);
    XFlush(d);
}

static void chord(const char *spec, int hold)
{
    char buf[256]; strncpy(buf, spec, sizeof buf - 1); buf[sizeof buf - 1] = 0;
    KeySym keys[8]; int n = 0;
    for (char *t = strtok(buf, "+"); t && n < 8; t = strtok(NULL, "+")) keys[n++] = sym(t);
    for (int i = 0; i < n; i++) { press(keys[i], True); usleep(30000); }
    usleep(hold * 1000);
    for (int i = n - 1; i >= 0; i--) { press(keys[i], False); usleep(30000); }
}

static void type_text(const char *s)
{
    for (; *s; s++) {
        char one[2] = { *s, 0 };
        KeySym k; Bool shift = False;
        if (*s == ' ') k = XK_space;
        else if (*s == '/') k = XK_slash;
        else if (*s == '.') k = XK_period;
        else if (*s == '-') k = XK_minus;
        else if (*s == '_') { k = XK_minus; shift = True; }
        else if (*s == ',') k = XK_comma;
        else if (*s == '=') k = XK_equal;
        else if (isupper((unsigned char)*s)) { one[0] = (char)tolower((unsigned char)*s); k = XStringToKeysym(one); shift = True; }
        else k = XStringToKeysym(one);
        if (shift) press(XK_Shift_L, True);
        press(k, True); usleep(15000); press(k, False); usleep(15000);
        if (shift) press(XK_Shift_L, False);
    }
}

static int shot(Window w, const char *path)
{
    XWindowAttributes a;
    if (!XGetWindowAttributes(d, w, &a)) return 1;
    XImage *img = XGetImage(d, w, 0, 0, a.width, a.height, AllPlanes, ZPixmap);
    if (!img) { fprintf(stderr, "XGetImage failed\n"); return 1; }
    FILE *f = fopen(path, "wb");
    if (!f) return 1;
    fprintf(f, "P6\n%d %d\n255\n", a.width, a.height);
    for (int y = 0; y < a.height; y++)
        for (int x = 0; x < a.width; x++) {
            unsigned long p = XGetPixel(img, x, y);
            unsigned char rgb[3] = { (unsigned char)((p & img->red_mask) >> 16),
                                     (unsigned char)((p & img->green_mask) >> 8),
                                     (unsigned char)(p & img->blue_mask) };
            fwrite(rgb, 1, 3, f);
        }
    fclose(f);
    XDestroyImage(img);
    return 0;
}

int main(int argc, char **argv)
{
    if (argc < 2) { fprintf(stderr, "usage: see source\n"); return 2; }
    d = XOpenDisplay(NULL);
    if (!d) { fprintf(stderr, "cannot open display\n"); return 1; }
    const char *cmd = argv[1];
    if (!strcmp(cmd, "find") && argc > 2) walk(DefaultRootWindow(d), argv[2]);
    else if (!strcmp(cmd, "activate") && argc > 2) {
        Window w = strtoul(argv[2], NULL, 0);
        XEvent e; memset(&e, 0, sizeof e);
        e.xclient.type = ClientMessage; e.xclient.window = w;
        e.xclient.message_type = XInternAtom(d, "_NET_ACTIVE_WINDOW", False);
        e.xclient.format = 32; e.xclient.data.l[0] = 2; e.xclient.data.l[1] = CurrentTime;
        XSendEvent(d, DefaultRootWindow(d), False, SubstructureRedirectMask | SubstructureNotifyMask, &e);
        XRaiseWindow(d, w); XFlush(d);
    }
    else if (!strcmp(cmd, "key") && argc > 2) chord(argv[2], argc > 3 ? atoi(argv[3]) : 60);
    else if (!strcmp(cmd, "down") && argc > 2) press(sym(argv[2]), True);
    else if (!strcmp(cmd, "up") && argc > 2) press(sym(argv[2]), False);
    else if (!strcmp(cmd, "type") && argc > 2) type_text(argv[2]);
    else if (!strcmp(cmd, "move") && argc > 3) { XTestFakeMotionEvent(d, -1, atoi(argv[2]), atoi(argv[3]), CurrentTime); XFlush(d); }
    else if (!strcmp(cmd, "click") && argc > 3) {
        int b = argc > 4 ? atoi(argv[4]) : 1;
        XTestFakeMotionEvent(d, -1, atoi(argv[2]), atoi(argv[3]), CurrentTime); XFlush(d); usleep(80000);
        XTestFakeButtonEvent(d, b, True, CurrentTime); XFlush(d); usleep(80000);
        XTestFakeButtonEvent(d, b, False, CurrentTime); XFlush(d);
    }
    else if (!strcmp(cmd, "rel") && argc > 3) {
        int dx = atoi(argv[2]), dy = atoi(argv[3]), steps = argc > 4 ? atoi(argv[4]) : 20;
        for (int i = 0; i < steps; i++) {
            int sx = dx * (i + 1) / steps - dx * i / steps, sy = dy * (i + 1) / steps - dy * i / steps;
            XTestFakeRelativeMotionEvent(d, sx, sy, CurrentTime); XFlush(d); usleep(16000);
        }
    }
    else if (!strcmp(cmd, "shot") && argc > 3) return shot(strtoul(argv[2], NULL, 0), argv[3]);
    else { fprintf(stderr, "bad command\n"); return 2; }
    XCloseDisplay(d);
    return 0;
}
