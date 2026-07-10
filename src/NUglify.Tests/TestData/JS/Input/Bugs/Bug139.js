const testString = {
    js: {
        createClass(className = "MyClass", withInitiator = true) {
            let output = `
class ${className} {
test() {
`;
            output += `} async init(){ console.log("Class loaded..");     } } `;
        }
    }
};