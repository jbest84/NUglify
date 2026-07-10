const sum = function ({ foo = 2, dummy = 3 }) {
	return foo + dummy;
};

const sum2 = ({ foo2 = 2, dummy2 = 3 }) => foo2 + dummy2;