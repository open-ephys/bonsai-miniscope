# Bonsai.Miniscope
[Bonsai](http://bonsai-rx.org/) package for controlling and acquiring data from head-borne miniscopes for calcium imaging. 

It contains the following nodes: 

- `LegacyUclaMiniscope`: UCLA Miniscope V3 using legacy DAQ firmware. Use this if you have an old DAQ box and have not updated firmware recently.
- `UclaMiniscopeV3`: UCLA Miniscope V3 using updated DAQ box and firmware. Use this if you are also using the V4 scope.
- `UclaMiniscopeV4`: UCLA Miniscope V4 with integrated IMU and remote focusing. Can be used with the [Open Ephys commutator](https://open-ephys.org/commutators/coaxial-commutator) for near zero-torque commutation.
- `UlcaMiniCam` : UCLA MiniCAM behavioral camera.

There are example Bonsai workflows demonstrating the use of this package in the ExampleWorkflows folder.

## License
[MIT](https://opensource.org/licenses/MIT)

If you use this project in your work, please reference this repository in any resulting papers or presentations.