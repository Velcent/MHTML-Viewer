(function(){
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

	const btn = document.createElement('a');
	btn.title = 'Toggle sidebar';
	btn.href = 'app://toggleSidebar';

	handle.appendChild(btn);
	document.body.appendChild(handle);

	// Get Document Titles & Sections
	let titles = Array.from(document.querySelectorAll('.breadcrumb-link')).filter(el => el.title && el.title.trim() !== '');
	titles.shift();
	let title = titles.map(el => el.title.trim()).join(' ⮞ ');
	title += (titles.length ? ' ⮞ ' : '') + (document.querySelector('.breadcrumb-item.active').textContent.trim() || '');
	const sections = document.querySelectorAll('h1[id], h2[id], h3[id], h4[id], h5[id], h6[id]');
	const sectionlist = document.querySelectorAll('.list-item .list-link');
	let data = null;
	getTitle = (frag = null) => {
		window.chrome.webview.postMessage({
			type: 'UpdateTitle'
		});
		let name = null;
		if (frag) frag = decodeURIComponent(frag);
		sections.forEach(sec => {
			const rect = sec.getBoundingClientRect();
			if (rect.top <= 100) {
				switch (sec.id) {
					case 'main':
					case 'content':
						break;
					default:
						frag = sec.id
				}
			}
			if (frag === sec.id) name = sec.textContent.trim();
		});
		name = (title ? title : '') + (name ? ' ⮞ ' + name : '');
		if (data === name) return;
		data = name;
		window.chrome.webview.postMessage({
			type: 'SetTitle',
			data: data
		});
		sectionlist.forEach(item => {
			const href = item.getAttribute('href');
			const url = new URL(href);
			const match = url.hash === '#' + frag;
			if (match) {
				item.classList.add('is-active');
			} else {
				item.classList.remove('is-active');
			}
		});
	}
})();