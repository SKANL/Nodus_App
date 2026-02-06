package org.shiny.bluetoothle;


public class ShinyBleCentralBroadcastReceiver
	extends crc643387a08acbe69b14.ShinyBroadcastReceiver
	implements
		mono.android.IGCUserPeer
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"";
		mono.android.Runtime.register ("Shiny.BluetoothLE.ShinyBleBroadcastReceiver, Shiny.BluetoothLE", ShinyBleCentralBroadcastReceiver.class, __md_methods);
	}

	public ShinyBleCentralBroadcastReceiver ()
	{
		super ();
		if (getClass () == ShinyBleCentralBroadcastReceiver.class) {
			mono.android.TypeManager.Activate ("Shiny.BluetoothLE.ShinyBleBroadcastReceiver, Shiny.BluetoothLE", "", this, new java.lang.Object[] {  });
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
