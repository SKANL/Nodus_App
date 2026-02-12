package crc643387a08acbe69b14;


public abstract class ShinyAndroidForegroundService_2
	extends crc643387a08acbe69b14.ShinyAndroidForegroundService
	implements
		mono.android.IGCUserPeer
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"";
		mono.android.Runtime.register ("Shiny.ShinyAndroidForegroundService`2, Shiny.Core", ShinyAndroidForegroundService_2.class, __md_methods);
	}

	public ShinyAndroidForegroundService_2 ()
	{
		super ();
		if (getClass () == ShinyAndroidForegroundService_2.class) {
			mono.android.TypeManager.Activate ("Shiny.ShinyAndroidForegroundService`2, Shiny.Core", "", this, new java.lang.Object[] {  });
		}
	}

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
