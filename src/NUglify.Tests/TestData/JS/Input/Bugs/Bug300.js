function test(p) {
	return __awaiter(this, void 0, void 0, function* () {
		let r = true;
		for (let n = 1; n <= 5; n++)
			if (!(yield isOk(p * n))) {
				r = false;
				break;
			}
		return r;
	});
}