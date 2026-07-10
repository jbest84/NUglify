"use strict";
const config = {
	...Manipulator.getDataAttributes(target),
	...Manipulator.getDataAttributes(this),
	...(true ? false : true),
	...(false ? true : false)
};