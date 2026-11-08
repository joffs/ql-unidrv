// The base of the program windows that must look right on a second screen with other scaling (refactor S11,
// 2026-11-08): the overrides each window repeated, in one place, so a new window can't forget them.
//   OnHandleCreated: Brand.MatchScreenDpi (a window whose handle exists early, e.g. the caption tint in the constructor).
//   OnLoad:          Brand.MatchScreenDpi again, before base.OnLoad, or after it when the window's own Load handlers
//                    place it on its screen (MatchAfterLoad); BeforeLoad runs between the check and base.OnLoad.
//   OnDpiChanged:    Brand.FollowFont (bold fonts made with new Font(...) follow the window's font).
// A window adds its own steps by overriding these and calling base first, as before.
using System;
using System.Windows.Forms;

namespace QlLabels
{
    class ScaledForm : Form
    {
        // true: check the screen after base.OnLoad, because the window's Load handlers place it (StatusForm, EditorForm).
        protected virtual bool MatchAfterLoad { get { return false; } }

        // Work that must run after the screen check but before the Load event (SetupForm: FitWidths).
        protected virtual void BeforeLoad() { }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Brand.MatchScreenDpi(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            if (MatchAfterLoad)
            {
                BeforeLoad();
                base.OnLoad(e);
                Brand.MatchScreenDpi(this);
            }
            else
            {
                Brand.MatchScreenDpi(this);
                BeforeLoad();
                base.OnLoad(e);
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Brand.FollowFont(this, Font);
        }
    }
}
