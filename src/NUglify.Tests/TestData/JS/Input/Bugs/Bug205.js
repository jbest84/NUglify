function test(a, b) {
	return {
		amethod() {
			return this;
		},
		get [a + b]() { },
		set [a + b](value) { },
		//[a + b](c) { },
		*[a + b](c) {
			return c;
		},
		async [a + b](c) {
			return c;
		},
		async*[a + b](c) {
			return c;
		}
	};
}