package crc64bd577c61b6f9d398;


public class AndroidLifecycleExecutor
	extends java.lang.Object
	implements
		mono.android.IGCUserPeer,
		androidx.lifecycle.LifecycleObserver
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"n_OnResume:()V:__export__\n" +
			"n_OnPause:()V:__export__\n" +
			"";
		mono.android.Runtime.register ("Shiny.Hosting.AndroidLifecycleExecutor, Shiny.Core", AndroidLifecycleExecutor.class, __md_methods);
	}

	public AndroidLifecycleExecutor ()
	{
		super ();
		if (getClass () == AndroidLifecycleExecutor.class) {
			mono.android.TypeManager.Activate ("Shiny.Hosting.AndroidLifecycleExecutor, Shiny.Core", "", this, new java.lang.Object[] {  });
		}
	}

@androidx.lifecycle.OnLifecycleEvent(androidx.lifecycle.Lifecycle.Event.ON_RESUME)
	public void OnResume ()
	{
		n_OnResume ();
	}

	private native void n_OnResume ();

@androidx.lifecycle.OnLifecycleEvent(androidx.lifecycle.Lifecycle.Event.ON_PAUSE)
	public void OnPause ()
	{
		n_OnPause ();
	}

	private native void n_OnPause ();

	private java.util.ArrayList refList;
	public void monodroidAddReference (java.lang.Object obj)
	{
		if (refList == null)
			refList = new java.util.ArrayList ();
		refList.add (obj);
	}

	public void monodroidClearReferences ()
	{
		if (refList != null)
			refList.clear ();
	}
}
