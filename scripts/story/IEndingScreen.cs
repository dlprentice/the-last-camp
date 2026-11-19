using System.Threading.Tasks;

namespace LastCamp.Story;

/// The closing sequence after the walk out: the shot over the clearing, the
/// credits and June's postcard. The HUD implements it; the session awaits it and
/// raises `credits_finished` when it completes. `fireKept` is the storm outcome.
public interface IEndingScreen
{
    Task ShowEnding(bool fireKept);
}
