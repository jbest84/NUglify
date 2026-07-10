fff = function () {
    if (true) {
	    let aaa = 1;
        try {
        }
        catch (bbb) {
            return aaa + bbb;
        }
    }
};

ggg = function () {
	var bbb = 1;
	if (true) {
		let aaa = 1;
		try {
		}
		catch (bbb) {
			let ccc = aaa + bbb;

			try {
			} catch (bbb) {
				return ccc + bbb;
			}
		}
	}
};