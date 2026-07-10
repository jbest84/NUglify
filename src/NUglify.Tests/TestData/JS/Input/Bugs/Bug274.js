class foo {
	#privateField = 4;
	static #privateStaticField = 6;

	#privateMethod() {
		return this.#privateField;
	}

	static #privateStaticMethod() {
		return #privateStaticField;
	}

	get #privateGetter() {
		return this.#privateField;
	}

	set #privateSetter(value) {
		this.#privateField = value;
	}
}