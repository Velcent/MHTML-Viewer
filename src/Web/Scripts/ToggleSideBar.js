/*
 * Injects the slim in-document handle used to toggle the native sidebar from inside the viewer WebView.
 */
(function(){
	// The helper is injected after each navigation, so make it idempotent.
	if (document.querySelector('.sidebarHandle')) return;

	const style = document.createElement('style');
	style.textContent = `
		.sidebarHandle {
			position: fixed;
			top: 0;
			left: -2px;
			width: 2px;
			height: 100%;
			cursor: ew-resize;
			z-index: 999999;
			background: transparent;
		}

		.sidebarHandle:hover {
			background: #39525a;
		}

		.sidebarHandle a {
			position: absolute;
			top: 50%;
			left: 12px;
			transform: translateY(-50%);
			width: 28px;
			height: 56px;
			display: flex;
			align-items: center;
			justify-content: center;
			background: #24272d;
			color: #d7dbe2;
			border: 1px solid #3d4148;
			border-right-color: #4a5058;
			border-radius: 8px;
			cursor: pointer;
			box-shadow: 0 8px 18px rgba(0,0,0,.24);
			text-decoration: none;
			line-height: 1;
			font-size: 18px;
			padding: 0;
		}

		.sidebarHandle a:hover {
			background: #303640;
			border-color: #59616c;
		}
	`;
	document.head.appendChild(style);

	const handle = document.createElement('div');
	handle.className = 'sidebarHandle';

	// Navigation to this app:// URL is intercepted by WebView.cs and converted into a sidebar toggle.
	const btn = document.createElement('a');
	btn.title = 'Toggle sidebar';
	btn.href = 'app://toggleSidebar';

	handle.appendChild(btn);
	document.body.appendChild(handle);
})();
